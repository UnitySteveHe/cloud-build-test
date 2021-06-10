using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.AutomatedQA;
using Unity.AutomatedQA.Editor;
using Unity.CloudTesting.Editor;
using Unity.RecordedTesting;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
[assembly:PostBuildCleanup(typeof(CloudTestPipeline))]

namespace Unity.CloudTesting.Editor
{
    public class CloudTestPipeline: IPostBuildCleanup
    {
        private string _testResultsPath = AutomatedQARuntimeSettings.PersistentDataPath;
        private static readonly TestRunnerApi TestRunnerInstance = ScriptableObject.CreateInstance<TestRunnerApi>();
        public static event Action testBuildFinished;

        private static string _buildPath;

        public static string BuildPath
        {
            get => string.IsNullOrEmpty(_buildPath) ? Path.Combine(AutomatedQARuntimeSettings.PersistentDataPath, "TestBuild.apk") : _buildPath;
            set => _buildPath = value;
        }

        public static bool IsRunningOnCloud()
        {
            return AutomatedQAEditorSettings.hostPlatform == HostPlatform.Cloud;
        }

        public static void SetTestRunOnCloud(bool enabled)
        {
            AutomatedQAEditorSettings.hostPlatform = enabled ? HostPlatform.Cloud : HostPlatform.Local;
        }
        
        public void Cleanup()
        {
            if (IsRunningOnCloud())
            {
                // This shouldn't be handled here...
                Debug.Log($"Build successfully saved at - {BuildPath}");
                
                AutomatedQAEditorSettings.ClearBuildFlags(EditorUserBuildSettings.selectedBuildTargetGroup);
                
                if (testBuildFinished != null)
                {
                    testBuildFinished.Invoke();
                }
                SetTestRunOnCloud(false);
            }
        }

        public static void MakeBuild(string[] testToExecute)
        {
            MakeBuild(testToExecute, EditorUserBuildSettings.activeBuildTarget);
        }

        public static void MakeBuild(string[] testToExecute, BuildTarget platform)
        {
            var filter = new Filter()
            {
                testMode = TestMode.PlayMode,
                targetPlatform = platform
            };
            if (testToExecute.Length > 0)
                filter.testNames = testToExecute;

            var settings = new ExecutionSettings(filter);
            TestRunnerInstance.Execute(settings);
        }

        public static Dictionary<string,List<string>> GetCloudTests()
        {
            var testFilterDictionary = new Dictionary<string, List<string>>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            //Change this to use fancy Select feature.
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
    
                foreach (var type in types)
                {
                    var methods = type.GetTypeInfo().DeclaredMethods;
                    foreach (var method in methods)
                    {
                        if (method.GetCustomAttributes(typeof(RecordedTestAttribute), false).Length > 0)
                        {
                            var assemblyName = assembly.GetName().Name;
                            if (!testFilterDictionary.ContainsKey(assemblyName))
                                testFilterDictionary.Add(assemblyName, new List<string>());
    
                            if (method.DeclaringType != null)
                            {
                                var methodNamespace = method.DeclaringType.Namespace;
                                var testEntry = methodNamespace == null
                                    ? method.DeclaringType.Name + "." + method.Name
                                    : method.DeclaringType.Namespace + "." + method.DeclaringType.Name + "." +
                                      method.Name;
                                testFilterDictionary[assemblyName]
                                    .Add(testEntry);
                                //   Debug.Log("Adding : " + method.DeclaringType.Name + "." + method.Name);
                            }
                        }
                    }
                }
            }
    
            return testFilterDictionary;
        }
        
        public static UploadUrlResponse GetUploadURL(string token = null)
        {
            Debug.Log("GetUploadURL");
            var projectId = Application.cloudProjectId;
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/builds?projectId={projectId}";

            var jsonObject = new GetUploadURLPayload();
            jsonObject.name = "PlayerWithTests.apk";
            jsonObject.description = "";
            string data = JsonUtility.ToJson(jsonObject);

            byte[] payload = GetBytes(data);
            UploadHandlerRaw uH = new UploadHandlerRaw(payload);
            uH.contentType = "application/json";
            var accessToken = token?? CloudProjectSettings.accessToken;

            using (var uwr = UnityWebRequest.Post(url, data))
            {
                uwr.uploadHandler = uH;
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Upload Build", "Get Upload URL", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    Debug.LogError($"Couldn't get signed url. Error - {uwr.error}");
                }
                else
                {
                    string response = uwr.downloadHandler.text;
                    Debug.Log($"response: {response}");
                    return JsonUtility.FromJson<UploadUrlResponse>(response);
                }

            }

            return new UploadUrlResponse();
        }

        public static UploadUrlResponse UploadBuild(string token = null)
        {
            var uploadInfo = GetUploadURL(token);
            UploadBuildToUrl(uploadInfo.upload_uri);
            
            return uploadInfo;
        }
        
        public static void UploadBuildToUrl(string uploadURL)
        {
            Debug.Log($"Upload Build - uploadURL: {uploadURL}");
            string buildpath = BuildPath;
            Debug.Log($"buildpath: {buildpath}");
            var payload = File.ReadAllBytes(buildpath);
            UploadHandlerRaw uH = new UploadHandlerRaw(payload);

            using (var uwr = UnityWebRequest.Put(uploadURL, payload))
            {
                uwr.uploadHandler = uH;
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone && !uwr.isDone)
                {
                    EditorUtility.DisplayProgressBar("Upload Build", "Uploading...", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    Debug.LogError($"Couldn't upload build. Error - {uwr.error}: {uwr.downloadHandler.text}");
                }
                else
                {
                    Debug.Log($"Build uploaded");
                }

            }
        }
        
        public static JobStatusResponse RunCloudTests(string buildId, List<string> cloudTests, string token = null)
        {
            Debug.Log($"RunCloudTests - buildId: {buildId}");
            var projectId = Application.cloudProjectId;
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/job/create?projectId={projectId}";
            var accessToken = token?? CloudProjectSettings.accessToken;

            var jsonObject = new CloudTestPayload();
            jsonObject.buildId = buildId;
            jsonObject.testNames = cloudTests;
            string data = JsonUtility.ToJson(jsonObject);

            byte[] payload = GetBytes(data);
            UploadHandlerRaw uH = new UploadHandlerRaw(payload);
            uH.contentType = "application/json";

            Debug.Log(url);
            Debug.Log(data);

            using (var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                uwr.uploadHandler = uH;
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);

                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Run Cloud Tests", "Starting tests", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    Debug.LogError($"Couldn't start cloud tests. Error - {uwr.error}");
                }
                else
                {
                    string response = uwr.downloadHandler.text;
                    Debug.Log($"response: {response}");
                    return JsonUtility.FromJson<JobStatusResponse>(response);
                }
            }

            return new JobStatusResponse();
        }

        public static JobStatusResponse GetJobStatus(string jobId, string token = null)
        {
            var projectId = Application.cloudProjectId;
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/jobs/{jobId}?projectId={projectId}";
            var accessToken = token?? CloudProjectSettings.accessToken;
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Get Job Status", "Refresh", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    Debug.LogError($"Couldn't get job status. Error - {uwr.error}: {uwr.downloadHandler.text}");
                    return new JobStatusResponse(jobId, "ERROR");
                }

                string response = uwr.downloadHandler.text;
                Debug.Log($"response: {response}");

                return JsonUtility.FromJson<JobStatusResponse>(response);
            }
        }
        
        public static TestResultsResponse GetTestResults(string jobId, string token = null)
        {
            var projectId = Application.cloudProjectId;
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/counters?jobId={jobId}&projectId={projectId}";
            var accessToken = token?? CloudProjectSettings.accessToken;
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Get Test Results", "Get Test Results", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.isNetworkError || uwr.isHttpError)
                {
                    Debug.LogError($"Couldn't get test results. Error - {uwr.error}: {uwr.downloadHandler.text}");
                    return new TestResultsResponse();
                }

                string response = uwr.downloadHandler.text;
                Debug.Log($"response: {response}");
                var testResults = JsonUtility.FromJson<TestResultsResponse>(response);
                testResults.rawResponse = response;
                string outputPath = Path.Combine(AutomatedQARuntimeSettings.PersistentDataPath, "CloudTestResults", $"TestResults-{jobId}.html");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, TestResultsToHTML(jobId, testResults));

                if (!Application.isBatchMode)
                {
                    EditorUtility.RevealInFinder(outputPath);
                }

                return testResults;
            }
        }

        private static string TestResultsToHTML(string jobId, TestResultsResponse data)
        {
            StringBuilder sb = new StringBuilder();
            string overallResult = data.allPass ? "PASS" : "FAIL";
            sb.Append($"<h1>Job ID: {jobId} </h1>");
            sb.Append($"<h2>Overall Result: {overallResult} </h2>");

            sb.Append($"<table>");
            foreach (var result in data.testResults)
            {
                foreach (var c in result.counters)
                {
                    sb.Append($"<tr>");

                    sb.Append($"<td>{result.deviceModel}</td>");
                    sb.Append($"<td>{result.testName}</td>");
                    sb.Append($"<td>{c._name}</td>");
                    string passfail = c._value == 1 ? "Pass" : "Fail";
                    string passfailstyle = c._value == 1 ? "background-color: lightgreen" : "background-color: red";
                    sb.Append($"<td style=\"{passfailstyle}\">{passfail}</td>");

                    sb.Append($"</tr>");
                }

            }
            sb.Append($"</table>");

            sb.Append($"<pre>{data.rawResponse}</pre>");


            return sb.ToString();
        }
        
        private static byte[] GetBytes(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return bytes;
        }
        
        [Serializable]
        public class UploadUrlResponse
        {
            public string id;
            public string upload_uri;
        }
        
        [Serializable]
        public class CloudTestPayload
        {
            public string buildId;
            public List<string> testNames;
        }
        
        [Serializable]
        public class JobStatusResponse
        {
            [SerializeField]
            public string jobId;
            public string status;

            public JobStatusResponse(string jobId, string status)
            {
                this.jobId = jobId;
                this.status = status;
            }

            public JobStatusResponse()
            {
                jobId = "";
                status = "UNKNOWN";
            }
        }
        
        [Serializable]
        public class TestCounter
        {
            public string _name;
            public int _value;
        }

        [Serializable]
        public class GetUploadURLPayload
        {
            public string name;
            public string description;
        }
        
        [Serializable]
        public class TestResult
        {
            public string testName;
            public string deviceModel;
            public TestCounter[] counters;
        }

        [Serializable]
        public class TestResultsResponse
        {
            public string rawResponse;
            public bool allPass;
            public TestResult[] testResults;
        }
    }
}