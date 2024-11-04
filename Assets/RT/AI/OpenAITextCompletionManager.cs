using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;


public class OpenAITextCompletionManager : MonoBehaviour
{

    public class GTPChatLine
    {
        public GTPChatLine(string role, string content)
        {
            _role = role;
            _content = content;
        }

        public string _role; //must be set to user, assistant, or system
        public string _content;
    }

    public void Start()
    {
       // ExampleOfUse();
    }

    //*  EXAMPLE START (this could be moved to your own code) */

    void ExampleOfUse()
    {
        //build a stack of GTPChatLine so we can add as many as we want

        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

        string openAI_APIKey = "put it here";

        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system", "The AI assistant will answer very rude and snarky and never helpful. Replies are short, usually under fifteen words."));
        lines.Enqueue(new GTPChatLine("user", "What is 1 + 1?"));

        string json = textCompletionScript.BuildChatCompleteJSON(lines);
        RTDB db = new RTDB();

        textCompletionScript.SpawnChatCompleteRequest(json, OnGTP3CompletedCallback, db, openAI_APIKey);
    }

   void OnGTP3CompletedCallback(RTDB db, JSONObject jsonNode)
    {

        if (jsonNode == null)
        {
            //must have been an error
            Debug.Log("Got callback! Data: " + db.ToString());
            RTQuickMessageManager.Get().ShowMessage(db.GetString("msg"));
            return;
        }
       
        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */

        string reply = jsonNode["choices"][0]["message"]["content"];
        RTQuickMessageManager.Get().ShowMessage(reply);

    }

    //*  EXAMPLE END */
    public bool SpawnChatCompleteRequest(string jsonRequest, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey)
    {

        StartCoroutine(GetRequest(jsonRequest, myCallback, db, openAI_APIKey));
        return true;
    }

    //Build OpenAI.com API request json
    public string BuildChatCompleteJSON(Queue<GTPChatLine> lines, int max_tokens = 100, float temperature = 1.3f, string model = "gpt-3.5-turbo")
    {

        string msg = "";

        //go through each object in lines
        foreach (GTPChatLine obj in lines)
        {
            if (msg.Length > 0)
            {
                msg += ",\n";
            }
            msg += "{\"role\": \"" + obj._role + "\", \"content\": \"" + SimpleJSON.JSONNode.Escape(obj._content) + "\"}";
        }

        string json =
         $@"{{
             ""model"": ""{model}"",
             ""messages"":[{msg}],
             ""temperature"": {temperature},
             ""max_tokens"": {max_tokens}
            }}";

        return json;
    }

    IEnumerator GetRequest(string json, Action<RTDB, JSONObject> myCallback, RTDB db, string openAI_APIKey)
    {

#if UNITY_STANDALONE && !RT_RELEASE 
               File.WriteAllText("text_completion_sent.json", json);
#endif
        string url;
        url = "https://api.openai.com/v1/chat/completions";
        //Debug.Log("Sending request " + url );

        using (var postRequest = UnityWebRequest.PostWwwForm(url, "POST"))
        {
            //Start the request with a method instead of the object itself
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.SetRequestHeader("Content-Type", "application/json");
            postRequest.SetRequestHeader("Authorization", "Bearer "+openAI_APIKey );
            // Here is the critical Line, in the postRequestScript we need a method to set the organization
            // look at file UnityWebRequest or PostRequest
            

            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                string msg = postRequest.error;
                Debug.Log(msg);
                //Debug.Log(postRequest.downloadHandler.text);
//#if UNITY_STANDALONE && !RT_RELEASE
                File.WriteAllText("last_error_returned.json", postRequest.downloadHandler.text);
//#endif
               
                db.Set("status", "failed");
                db.Set("msg", msg);
                myCallback.Invoke(db, null);
            }
            else
            {

#if UNITY_STANDALONE && !RT_RELEASE 
//                Debug.Log("Form upload complete! Downloaded " + postRequest.downloadedBytes);

                File.WriteAllText("textgen_json_received.json", postRequest.downloadHandler.text);
#endif

                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                yield return null; //wait a frame to lesson the jerkiness

                Debug.Assert(rootNode.Tag == JSONNodeType.Object);

                db.Set("status", "success");
                myCallback.Invoke(db, (JSONObject)rootNode);
               
            }
        }
    }

        //ASSISTANT FUNCTIONALITY

    public void CreateThread(string apiKey, Action<RTDB, JSONObject> callback)
    {
        //Logger.Log("Creating thread...");
        StartCoroutine(PostRequest("https://api.openai.com/v1/threads", "{}", apiKey, callback));
    }

    public void AddMessageToThread(string threadId, string role, string content, string apiKey, Action<RTDB, JSONObject> callback)
    {
        string json = $@"{{
            ""role"": ""{role}"",
            ""content"": ""{SimpleJSON.JSONNode.Escape(content)}""
        }}";
        string url = $"https://api.openai.com/v1/threads/{threadId}/messages";
        //Logger.Log($"Adding message to thread. URL: {url} Payload: {json}");
        StartCoroutine(PostRequest(url, json, apiKey, callback));
    }

    public void CreateRun(string threadId, string assistantId, string instructions, string apiKey, Action<RTDB, JSONObject> callback)
    {
        string json = $@"{{
            ""assistant_id"": ""{assistantId}"",
            ""instructions"": ""{SimpleJSON.JSONNode.Escape(instructions)}""
        }}";
        string url = $"https://api.openai.com/v1/threads/{threadId}/runs";
        //Logger.Log($"Creating run for thread. URL: {url} Payload: {json}");
        StartCoroutine(PostRequest(url, json, apiKey, callback));
    }

    public void GetMessagesFromThread(string threadId, string apiKey, Action<RTDB, JSONObject> callback)
    {
        string url = $"https://api.openai.com/v1/threads/{threadId}/messages";
        //Logger.Log($"Getting messages from thread. URL: {url}");
        StartCoroutine(GetRequest(url, apiKey, callback));
    }

    private IEnumerator PostRequest(string url, string json, string apiKey, Action<RTDB, JSONObject> callback)
    {
        using (UnityWebRequest postRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            postRequest.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            postRequest.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            postRequest.SetRequestHeader("Content-Type", "application/json");
            postRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            postRequest.SetRequestHeader("OpenAI-Beta", "assistants=v2");

            //Logger.Log("Sending POST request to: " + url);
            //Logger.Log("Request payload: " + json);

            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                //Logger.LogError("Post request error: " + postRequest.error);
                //Logger.LogError("Post request response: " + postRequest.downloadHandler.text);
                RTDB db = new RTDB();
                db.Set("status", "failed");
                db.Set("msg", postRequest.error);
                callback.Invoke(db, null);
            }
            else
            {
                //Logger.Log("Post request successful: " + postRequest.downloadHandler.text);
                JSONNode rootNode = JSON.Parse(postRequest.downloadHandler.text);
                RTDB db = new RTDB();
                db.Set("status", "success");
                callback.Invoke(db, (JSONObject)rootNode);
            }
        }
    }

    private IEnumerator GetRequest(string url, string apiKey, Action<RTDB, JSONObject> callback)
    {
        using (UnityWebRequest getRequest = UnityWebRequest.Get(url))
        {
            getRequest.SetRequestHeader("Content-Type", "application/json");
            getRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            getRequest.SetRequestHeader("OpenAI-Beta", "assistants=v2");
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityWebRequest.Result.Success)
            {
                //Logger.LogError("Get request error: " + getRequest.error);
                //Logger.LogError("Get request response: " + getRequest.downloadHandler.text);
                RTDB db = new RTDB();
                db.Set("status", "failed");
                db.Set("msg", getRequest.error);
                callback.Invoke(db, null);
            }
            else
            {
                //Logger.Log("Get request successful: " + getRequest.downloadHandler.text);
                JSONNode rootNode = JSON.Parse(getRequest.downloadHandler.text);
                RTDB db = new RTDB();
                db.Set("status", "success");
                callback.Invoke(db, (JSONObject)rootNode);
            }
        }
    }

    public void GetRunStatus(string threadId, string runId, string apiKey, Action<RTDB, JSONObject> callback)
{
    string url = $"https://api.openai.com/v1/threads/{threadId}/runs/{runId}";
    //Logger.Log($"Getting run status. URL: {url}");
    StartCoroutine(GetRequest(url, apiKey, callback));
}

}
