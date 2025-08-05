using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AutomatedQA;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TestPlatforms.Cloud
{
    public interface ICloudTestClient
    {
        UploadUrlResponse GetUploadURL();
        UploadUrlResponse GetUploadURL(string accessToken, string projectId);
        void UploadBuildToUrl(string uploadURL, string buildPath);
        UploadUrlResponse UploadBuild(string buildPath, string accessToken, string projectId);
        JobStatusResponse RunCloudTests(string buildId, List<string> cloudTests);
        JobStatusResponse RunCloudTests(string buildId, List<string> cloudTests, string accessToken, string projectId);
        JobStatusResponse GetJobStatus(string jobId);
        JobStatusResponse GetJobStatus(string jobId, string accessToken, string projectId);
        TestResultsResponse GetTestResults(string jobId);
        TestResultsResponse GetTestResults(string jobId, string accessToken, string projectId);
    }

    public class CloudTestClient : ICloudTestClient
    {
        public UploadUrlResponse GetUploadURL()
        {
            return GetUploadURL(CloudProjectSettings.accessToken, Application.cloudProjectId);
        }

        public UploadUrlResponse GetUploadURL(string accessToken, string projectId)
        {
            Debug.Log("GetUploadURL");
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/builds?projectId={projectId}";

            var jsonObject = new GetUploadURLPayload();
#if UNITY_IOS
            jsonObject.buildType = "IOS";
#else
            jsonObject.buildType = "ANDROID";
#endif
            jsonObject.name = CloudTestConfig.BuildName;
            jsonObject.description = "";
            string data = JsonUtility.ToJson(jsonObject);

            byte[] payload = GetBytes(data);
            UploadHandlerRaw uH = new UploadHandlerRaw(payload);
            uH.contentType = "application/json";

            using (var uwr = UnityWebRequest.PostWwwForm(url, data))
            {
                uwr.uploadHandler = uH;
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Upload Build", "Get Upload URL", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.IsError())
                {
                    HandleError($"Couldn't get signed url. Error - {uwr.error}");
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

        public UploadUrlResponse UploadBuild(string buildPath, string accessToken, string projectId)
        {
            var uploadInfo = GetUploadURL(accessToken, projectId);
            UploadBuildToUrl(uploadInfo.upload_uri, buildPath);

            return uploadInfo;
        }

        public void UploadBuildToUrl(string uploadURL, string buildPath)
        {
            Debug.Log($"Upload Build - uploadURL: {uploadURL}");
            Debug.Log($"buildpath: {buildPath}");
            var payload = File.ReadAllBytes(buildPath);
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

                if (uwr.IsError())
                {
                    HandleError($"Couldn't upload build. Error - {uwr.error}: {uwr.downloadHandler.text}");
                }
                else
                {
                    Debug.Log($"Build uploaded");
                }
            }
        }

        public JobStatusResponse RunCloudTests(string buildId, List<string> cloudTests)
        {
            return RunCloudTests(buildId, cloudTests, CloudProjectSettings.accessToken, Application.cloudProjectId);
        }

        public JobStatusResponse RunCloudTests(string buildId, List<string> cloudTests, string accessToken, string projectId)
        {
            Debug.Log($"RunCloudTests - buildId: {buildId}");
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/job/create?projectId={projectId}";

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

                if (uwr.IsError())
                {
                    HandleError($"Couldn't start cloud tests. Error - {uwr.error}");
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

        public JobStatusResponse GetJobStatus(string jobId)
        {
            return GetJobStatus(jobId, CloudProjectSettings.accessToken, Application.cloudProjectId);
        }

        public JobStatusResponse GetJobStatus(string jobId, string accessToken, string projectId)
        {
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/jobs/{jobId}?projectId={projectId}";
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Get Job Status", "Refresh", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.IsError())
                {
                    HandleError($"Couldn't get job status. Error - {uwr.error}: {uwr.downloadHandler.text}");
                    return new JobStatusResponse(jobId, "ERROR");
                }

                string response = uwr.downloadHandler.text;
                Debug.Log($"response: {response}");

                return JsonUtility.FromJson<JobStatusResponse>(response);
            }
        }

        public TestResultsResponse GetTestResults(string jobId)
        {
            return GetTestResults(jobId, CloudProjectSettings.accessToken, Application.cloudProjectId);
        }

        public TestResultsResponse GetTestResults(string jobId, string accessToken, string projectId)
        {
            var url = $"{AutomatedQARuntimeSettings.DEVICE_TESTING_API_ENDPOINT}/v1/counters?jobId={jobId}&projectId={projectId}";
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.SetRequestHeader("Authorization", "Bearer " + accessToken);
                AsyncOperation request = uwr.SendWebRequest();

                while (!request.isDone)
                {
                    EditorUtility.DisplayProgressBar("Get Test Results", "Get Test Results", request.progress);
                }
                EditorUtility.ClearProgressBar();

                if (uwr.IsError())
                {
                    HandleError($"Couldn't get test results. Error - {uwr.error}: {uwr.downloadHandler.text}");
                    return new TestResultsResponse();
                }

                string response = uwr.downloadHandler.text;
                Debug.Log($"response: {response}");
                var testResults = JsonUtility.FromJson<TestResultsResponse>(response);
                testResults.rawResponse = response;
                string outputPath = Path.Combine(AutomatedQARuntimeSettings.PersistentDataPath, "CloudTestResults", $"TestResults-{jobId}.html");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, TestResultsToHTML(jobId, testResults));
                TestResultsToXML(testResults);

                if (!Application.isBatchMode)
                {
                    EditorUtility.RevealInFinder(outputPath);
                }

                return testResults;
            }
        }

        private static void HandleError(string msg)
        {
            if (Application.isBatchMode)
            {
                throw new Exception(msg);
            }
            Debug.LogError(msg);
        }

        private static byte[] GetBytes(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return bytes;
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
                    sb.Append($"<td>{result.deviceName}</td>");
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

        private static void TestResultsToXML(TestResultsResponse data)
        {
            var tests = new List<ReportingManager.TestData>();
            foreach (var result in data.testResults)
            {
                foreach (var c in result.counters)
                {
                    var testData = new ReportingManager.TestData();
                    testData.TestName = $"{result.deviceModel}:{result.deviceName}:{result.testName}:{c._name}";
                    testData.Status = c._value == 1 ? ReportingManager.TestStatus.Pass.ToString() : ReportingManager.TestStatus.Fail.ToString();
                    tests.Add(testData);
                }
            }

            ReportingManager.GenerateXmlReport(tests, Path.Combine(CloudTestConfig.BuildFolder, "cloud-test-report.xml"));
        }
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
    public class BundleUpload
    {
        public string buildPath;
        public string buildName;
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

        public bool IsInProgress()
        {
            return status != "COMPLETED" && status != "ERROR" && status != "UNKNOWN";
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
        public string buildType;
    }

    [Serializable]
    public class TestResult
    {
        public string testName;
        public string deviceModel;
        public string deviceName;
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