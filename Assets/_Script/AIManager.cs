//If you've bought the Salsa Suite plugin and installed it, you should uncomment the next line to enable lipsyncing.
//If you don't have it, comment it out, it should compile, but without the lipsyncing and eye movements.
#define CRAZY_MINNOW_PRESENT

#if CRAZY_MINNOW_PRESENT
using CrazyMinnow.SALSA;
#endif

using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;


using static OpenAITextCompletionManager;

public class AIManager : MonoBehaviour
{

    public MicRecorder _microPhoneScript;
    string _openAI_APIKey;
    string _openAI_APIModel;
    string _googleAPIkey;
    string _elevenLabsAPIkey;
    public GameObject _visuals;
    AudioSource _audioSourceToUse = null;
    Vector2 vTextOverlayPos = new Vector2(Screen.width * 0.58f, (float)Screen.height - ((float)Screen.height * 0.4f));
    Vector2 vStatusOverlayPos = new Vector2(Screen.width * 0.44f, (float)Screen.height - ((float)Screen.height * 1.1f));
    public TMPro.TextMeshProUGUI _dialogText;
    public TMPro.TextMeshProUGUI _statusText;

    public static string pacientThreadId { get; set; }
    public static string doctorThreadId { get; set; }
    private bool runCompleted = false;
    private CancellationTokenSource pollingCancellationTokenSource;
    private Coroutine pollingCoroutine;

    Queue<GTPChatLine> _chatHistory = new Queue<GTPChatLine>();
   
    public Button _recordButton;
    // Start is called before the first frame update

    Friend _activeFriend;
    Animator _animator = null;

    public TMPro.TextMeshProUGUI _friendNameGUI;

    private void OnDestroy()
    {

    }

    public void SetActiveFriend(Friend newFriend)
    {
        _activeFriend = newFriend;
        if (newFriend == null) return;
        _audioSourceToUse = gameObject.GetComponent<AudioSource>();
        _friendNameGUI.text = _activeFriend._name;
        
        if (_friendNameGUI.text == "Unset")
        {
            _dialogText.text = "Before running this, edit the config_template.txt file to set your API keys, then rename the file to config.txt!";
            return;
        }

        _dialogText.text = "Click Start for the character to introduce themselves.";
        _statusText.text = "";

        ForgetStuff();

         List<GameObject> objs = new List<GameObject> ();
        RTUtil.AddObjectsToListByNameIncludingInactive(_visuals, "char_visual", true, objs);

        foreach (GameObject obj in objs)
        {
            obj.SetActive(false);
        }

        //turn on the one we need
        var activeVisual = RTUtil.FindInChildrenIncludingInactive(_visuals, "char_visual_" + _activeFriend._visual);
        if (activeVisual != null)
        {
            activeVisual.SetActive(true);
        }

        #if CRAZY_MINNOW_PRESENT

        //see if it has a model we should sent the wavs to for lipsyncing
        var lipsyncModel = activeVisual.GetComponentInChildren<Salsa>();

        if (lipsyncModel != null)
        {
            Debug.Log("Found salsa");
            //_salsa
            _audioSourceToUse = lipsyncModel.GetComponent<AudioSource>();
        }
        _animator = activeVisual.GetComponentInChildren<Animator>();
#endif
        SetListening(false);
       
    }

    void SetListening(bool bNew)
    {
        if (_animator != null)
        {
            _animator.SetBool("Listening", bNew);
        }
    }

    void SetTalking(bool bNew)
    {
        if (_animator != null)
        {
            _animator.SetBool("Talking", bNew);
        }
    }
    public void SetGoogleAPIKey(string key)
    {
        _googleAPIkey = key;
    }

    public void SetOpenAI_APIKey(string key)
    {
        _openAI_APIKey = key;
    }
    public void SetOpenAI_Model(string model)
    {
        _openAI_APIModel = model;
    }

    public void SetElevenLabsAPIKey(string key)
    {
        _elevenLabsAPIkey = key;
    }

    public string GetAdvicePrompt()
    {
       return _activeFriend._advicePrompt;
    }

    public void ModFriend(int mod)
    {
        int curFriendIndex = _activeFriend._index;

        //mod the current friend index by mod, but make sure it's not less than
        //0 and more than Config.Get().GetFriendCount()
        int newFriendIndex = (curFriendIndex + mod) % Config.Get().GetFriendCount();
        if (newFriendIndex < 0) newFriendIndex = Config.Get().GetFriendCount() - 1;
        SetActiveFriend(Config.Get().GetFriendByIndex(newFriendIndex));

    }
    public void MakeFriendInvisible()
{
    if (_activeFriend == null) return;

    // Get the GameObject associated with the active friend's visual
    var activeVisual = RTUtil.FindInChildrenIncludingInactive(_visuals, "char_visual_doctor");
    if (activeVisual != null)
    {
        // Make the friend invisible by disabling the GameObject
        activeVisual.SetActive(false);
        
        // Alternatively, if you want to keep the GameObject active but make the friend invisible,
        // you can disable the renderer components instead:
        /*
        Renderer[] renderers = activeVisual.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }
        */
    }
    else
    {
        Debug.LogWarning("No visual found for the active friend.");
    }

    _dialogText.text = $"{_activeFriend._name} is now invisible.";
}
public void MakeFriendVisible()
{
    if (_activeFriend == null) return;

    // Get the GameObject associated with the active friend's visual
    var activeVisual = RTUtil.FindInChildrenIncludingInactive(_visuals, "char_visual_doctor");
    if (activeVisual != null)
    {
        // Make the friend visible by enabling the GameObject
        activeVisual.SetActive(true);

        // Alternatively, if you disabled the renderers to make the friend invisible,
        // you can re-enable the renderer components instead:
        /*
        Renderer[] renderers = activeVisual.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = true;
        }
        */
    }
    else
    {
        Debug.LogWarning("No visual found for the active friend.");
    }

    _dialogText.text = $"{_activeFriend._name} is now visible.";
}


    public void PlayClickSound()
    {
        RTMessageManager.Get().Schedule(0, RTAudioManager.Get().PlayEx, "muffled_bump", 0.5f ,1.0f, false, 0.0f);
    }
    public void OnPreviousFriend()
    {
        PlayClickSound();
        ModFriend(-1);
    }
    public void OnNextFriend()
    {
        PlayClickSound();
        ModFriend(1);
    }

    void Start()
    {
        StartPacientAssistantInteraction("Guten Tag, was ist ihre Beschwerde?");
    }

    public int CountWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        // Split the input into words and return the count
        string[] words = input.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }

    string GetBasePrompt()
    {
        return _activeFriend._basePrompt;
    }

    string GetDirectionPrompt()
    {
        return _activeFriend._directionPrompt;
    }

    void TrimHistoryIfNeeded()
    {
        int tokenSize = CountWords(GetBasePrompt());
        int historyTokenSize = 0;
        //tokenSize of all words in _chatHistory
        foreach (GTPChatLine line in _chatHistory)
        {
            historyTokenSize += CountWords(line._content);
        }

        int maxTokenUseForPromptsAndHistory = tokenSize+ _activeFriend._friendTokenMemory; //too high and the text gets... corrupted...

        if (tokenSize + historyTokenSize > maxTokenUseForPromptsAndHistory)
        {
            //remove oldest lines until we are under the max
            while (tokenSize + historyTokenSize > maxTokenUseForPromptsAndHistory)
            {
                //we always remove things in pairs, the request, and the answer.

                GTPChatLine line = _chatHistory.Dequeue();
                historyTokenSize -= CountWords(line._content);
                line = _chatHistory.Dequeue();
                historyTokenSize -= CountWords(line._content);
                line = _chatHistory.Dequeue();
                historyTokenSize -= CountWords(line._content);
            }
        }

        Debug.Log("Prompt tokens: " + tokenSize + " History token size:" + historyTokenSize);

    }
    void GetGPT3Text(string question)
    {
        //build a stack of GTPChatLine so we can add as many as we want

        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();
        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system", GetBasePrompt()));
    
        TrimHistoryIfNeeded();
        //inject chat history
        foreach (GTPChatLine line in _chatHistory)
        {
            lines.Enqueue(line);
        }

        lines.Enqueue(new GTPChatLine("system", GetDirectionPrompt()));

        //the new question
        lines.Enqueue(new GTPChatLine("user", question));
    
        string json = textCompletionScript.BuildChatCompleteJSON(lines, _activeFriend._maxTokensToGenerate, _activeFriend._temperature, _openAI_APIModel);
        RTDB db = new RTDB();
        db.Set("question", question);
        db.Set("role", "user");

        textCompletionScript.SpawnChatCompleteRequest(json, OnGPT3TextCompletedCallback, db, _openAI_APIKey);
        UpdateStatusText(RTUtil.ConvertSansiToUnityColors("(AI is thinking) You said: `$" + question + "``"), 20);
    }

    void OnGPT3TextCompletedCallback(RTDB db, JSONObject jsonNode)
    {

        if (jsonNode == null)
        {
            //must have been an error
            Debug.Log("Got callback! Data: " + db.ToString());
            UpdateStatusText(db.GetString("msg"));
            return;
        }

        /*
        foreach (KeyValuePair<string, JSONNode> kvp in jsonNode)
        {
            Debug.Log("Key: " + kvp.Key + " Val: " + kvp.Value);
        }
        */
        string reply = jsonNode["choices"][0]["message"]["content"];
        if (reply.Length < 5)
        {
            Debug.Log("Error parsing reply: " + reply);
            db.Set("english", "Error. I don't know what to say.");
            db.Set("japanese", "エラーです。なんて言っていいのかわからない。");
            SayText(db);
            return;
        }

            //just whatever is there is fine
            db.Set("english", reply);
            db.Set("japanese", reply);
       
        //Let's say it
        SayText(db);

        _chatHistory.Enqueue(new GTPChatLine(db.GetString("role"), db.GetString("question")));
        _chatHistory.Enqueue(new GTPChatLine("assistant", reply));

    }

    void SayText(RTDB db)
    {
      
        string text = db.GetString(_activeFriend._language);
        string json;
        int sampleRate = 22050;

        if (_activeFriend._elevelLabsVoice.Length > 1 && _elevenLabsAPIkey.Length > 1)
        {
            //get the country code directly from the voice name. This should always work, I hope
            string countryCode = _activeFriend._elevelLabsVoice.Substring(0, 5);
            ElevenLabsTextToSpeechManager ttsScript = gameObject.GetComponent<ElevenLabsTextToSpeechManager>();
            json = ttsScript.BuildTTSJSON(text, _activeFriend._elevenlabsStability);
            ttsScript.SpawnTTSRequest(json, OnTTSCompletedCallbackElevenLabs, db, _elevenLabsAPIkey, _activeFriend._elevelLabsVoice);

            UpdateStatusText("Clearing throat...", 20);

        }
        else if (_activeFriend._googleVoice.Length > 1 && _googleAPIkey.Length > 1)
        {
            //get the country code directly from the voice name. This should always work, I hope
            string countryCode = _activeFriend._googleVoice.Substring(0, 5);
            GoogleTextToSpeechManager ttsScript = gameObject.GetComponent<GoogleTextToSpeechManager>();
            json = ttsScript.BuildTTSJSON(text, countryCode, _activeFriend._googleVoice, sampleRate, _activeFriend._pitch, _activeFriend._speed);
            ttsScript.SpawnTTSRequest(json, OnTTSCompletedCallback, db, _googleAPIkey);
            UpdateStatusText("Clearing throat...", 20);
        } else
        {
            //No text to speech setup for this voice

            UpdateDialogText(db.GetString("japanese"));
            UpdateStatusText("");
        }
    }

    void OnTTSCompletedCallback(RTDB db, byte[] wavData)
    {
        if (wavData == null)
        {
            Debug.Log("Error getting wav: " + db.GetString("msg"));
           
        } else
        {
            GoogleTextToSpeechManager ttsScript = gameObject.GetComponent<GoogleTextToSpeechManager>();
            AudioSource audioSource = _audioSourceToUse;
            audioSource.clip = ttsScript.MakeAudioClipFromWavFileInMemory(wavData);
            audioSource.Play();

        }


        UpdateDialogText(db.GetString("japanese"));
        UpdateStatusText("");
    }


    void OnTTSCompletedCallbackElevenLabs(RTDB db, AudioClip clip)
    {
        if (clip == null)
        {
            Debug.Log("Error getting wav: " + db.GetString("msg"));
          
        } else
        {
            ElevenLabsTextToSpeechManager ttsScript = gameObject.GetComponent<ElevenLabsTextToSpeechManager>();
            AudioSource audioSource = _audioSourceToUse;
            audioSource.clip = clip;
            audioSource.Play();
        }
  

        UpdateDialogText(db.GetString("japanese"));
        UpdateStatusText("");
    }

    public void ProcessMicAudioByFileName(string fAudioFileName)
    {
        OpenAISpeechToTextManager speechToTextScript = gameObject.GetComponent<OpenAISpeechToTextManager>();

        byte[] fileBytes = System.IO.File.ReadAllBytes(fAudioFileName);
        string prompt = "";

        RTDB db = new RTDB();

        //let's add strings from the recent conversation to the prompt text
        foreach (GTPChatLine line in _chatHistory)
        {
            prompt += line._content + "\n";
            if (prompt.Length > 180)
            {
                //whisper will only processes the last 200 words I read
                break;
            }
        }

        if (prompt == "")
        {
            //no history yet?  Ok, use the base prompt, better than nothing
            prompt = _activeFriend._basePrompt;
        }
        

        speechToTextScript.SpawnSpeechToTextRequest(prompt, OnSpeechToTextCompletedCallback, db, _openAI_APIKey, fileBytes);
        UpdateStatusText("Understanding speech...", 20);

    }

   

    void OnSpeechToTextCompletedCallback(RTDB db, JSONObject jsonNode)
    {
        if (jsonNode == null)
        {
            Debug.Log("Got callback! Data: " + db.ToString());
            UpdateStatusText(db.GetString("msg"));
            return;
        }
        string reply = jsonNode["text"];
        UpdateStatusText("Heard: "+reply);
        if(_friendNameGUI.text == "Pacient"){
            StartPacientAssistantInteraction(reply);
        }else if(_friendNameGUI.text == "Doctor"){
            StartDoctorAssistantInteraction(reply);
        }else{
            //GetGPT3Text(reply);
        }
        //GetGPT3Text(reply);
        //StartAssistantInteraction(reply);
    }

    public void ToggleRecording()
    {
        if (!_microPhoneScript.IsRecording())
        {
            StopTalking();
            Debug.Log("Recording started");
            //make the button background turn red
            _recordButton.GetComponent<Image>().color = Color.red;
            _microPhoneScript.StartRecording();
            PlayClickSound();
            SetListening(true);

        }
        else
        {
            //Turn the button background color back
            _recordButton.GetComponent<Image>().color = Color.white;
            PlayClickSound();

            Debug.Log("Recording stopped");

            //let's set the filename to a temporary space that will work on iOS
            string outputFileName = Application.temporaryCachePath + "/temp.wav";
            //string outputFileName = Application.persistentDataPath + "/temp.wav"; // this seems to work in WEBGL but the path above taken by Seth also works

            _microPhoneScript.StopRecordingAndProcess(outputFileName);
            SetListening(false);

        }
    }

    public void OnSceneStop(){
        _microPhoneScript.StopRecordingDONTProcess();
        UpdateStatusText("Ich denke...");
    }

    public void OnStopButton()
    {
        PlayClickSound();
        StopTalking();
    }

    public void OnCopyButton()
    {
        PlayClickSound();
        string text = _dialogText.text;
        if (text.Length > 0)
        {
            GUIUtility.systemCopyBuffer = text;
            UpdateStatusText("Copied to clipboard");
        } else
        {
            UpdateStatusText("Nothing to copy");
        }

    }
    public void OnAdviceButton()
    {
        ForgetStuff();
        //build a stack of GTPChatLine so we can add as many as we want
        PlayClickSound();

        OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();
        Queue<GTPChatLine> lines = new Queue<GTPChatLine>();
        lines.Enqueue(new GTPChatLine("system", GetBasePrompt()));

        TrimHistoryIfNeeded();
        //inject chat history
        foreach (GTPChatLine line in _chatHistory)
        {
            lines.Enqueue(line);
        }

        string question = GetAdvicePrompt();

        //remind it about the format
        lines.Enqueue(new GTPChatLine("system", GetDirectionPrompt()));
        //the new question
        lines.Enqueue(new GTPChatLine("system", question));

        string json = textCompletionScript.BuildChatCompleteJSON(lines, _activeFriend._maxTokensToGenerate, _activeFriend._temperature, _openAI_APIModel);
        RTDB db = new RTDB();
        db.Set("role", "system");
        db.Set("question", question);
        textCompletionScript.SpawnChatCompleteRequest(json, OnGPT3TextCompletedCallback, db, _openAI_APIKey);
        UpdateStatusText(RTUtil.ConvertSansiToUnityColors("Thinking..."), 20);
        UpdateDialogText("");
    }

    public void StopTalking()
    {
        AudioSource audioSource = _audioSourceToUse;
        audioSource.Stop();
        SetTalking(false);

    }
    public void ForgetStuff()
    {
        _chatHistory.Clear();
        StopTalking();

    }
    public void OnForgetButton()
    {
        //Clear chat history
        PlayClickSound();   //write a message about it
        //RTQuickMessageManager.Get().ShowMessage("Chat history cleared");
        ForgetStuff();
    }

    public int GetFriendIndex()
    {
        if (_activeFriend == null)
            return 0;
        else
            return _activeFriend._index;

    }

    void UpdateStatusText(string msg, float timer = 3)
    {
        _statusText.text = msg;
    }

    void UpdateDialogText(string msg)
    {
        _dialogText.text = msg;
    }

    private void Update()
    {
        if (_audioSourceToUse != null) 
        {
            SetTalking(_audioSourceToUse.isPlaying);
        }

    }
   

    //ASSISTANT FUNCTIONALITY

public void StartPacientAssistantInteraction(string userMessage)
{
    OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

    string apiKey = _openAI_APIKey; // Ensure your API key is set
    string assistantId = "asst_B0LxMmnDpoXMMOtEzpkAffQC"; // Update with the correct assistant ID
    string instructions = "KEEP YOUR RESPONSES LIMITED WITHIN A PARAGRAPH. DO NOT USE ANY SPECIAL FORMATTING. KEEP THE CONVERSATION IN GERMAN. INSTRUCTIONS: Wir führen ein medizinisches Rollenspiel einer Anamnesesituation durch. Du spielst Annette Klein nach der Fallbeschreibung. Du beginnst die Konversation mit dem Text, der auf folgende Frage antwortet (die nicht gestellt wird): „Guten Morgen Frau Klein. Ich bin heute für Sie in der Notaufnahme zuständig. Was kann ich denn für Sie tun?“ Dein Text zu Beginn ist also der Folgende: Mir geht es so schlecht seit gestern nachmittag. Gestern früh habe ich mich noch fit gefühlt, ich war sogar noch beim Joggen, aber am Nachmittag habe ich dann innerhalb ein paar Stunden so hohes Fieber und ganz schreckliche Kopfschmerzen bekommen. Und als ich heute Nacht im Bett lag, hatte ich das Gefühl, dass ich gar nicht richtig Luft kriege, ich hatte immer so Alpträume, dass ich ertrinke. ENDE VON ANFANGSTEXT.Daraufhin antwortest du wortgenau auf die Fragen, die dir vom Chatpartner gestellt werden. Medizinischer Fall: Sie sind Anette Klein, eine etwa 55-jährige Frau, studierte Biologielehrerin, arbeiten aber seit der Geburt der Kinder nicht mehr in Ihrem Beruf, sondern helfen bei Ihrem Mann in seiner Anwaltskanzlei aus. Sie haben zwei erwachsene Töchter, hatten zwischen den beiden Schwangerschaften eine Fehlgeburt. Die Ausschabung nach dieser Fehlgeburt ist die einzige Operation, die Sie jemals hatten. Gesundheitlich sind Sie seit einiger Zeit heftig mit den Wechseljahren beschäftigt, die Ihnen starke Beschwerden machen. Vor allem die Schweißausbrüche finden Sie schlimm, die Sie für die Infekte verantwortlich machen, die Sie seit einiger Zeit immer wieder haben. Nachts ist es zwar nicht so schlimm wie tagsüber, aber trotzdem schwitzen Sie so etwa einmal die Woche nachts so stark, dass Sie sich umziehen müssen. Außerdem haben Sie eine Anämie (Blutarmut), weil Sie immer noch sehr starke Menstruationsblutungen haben. Ansonsten sind Sie gesundheitlich eigentlich gut beieinander - seit etwa 10 Jahren nehmen Sie Tabletten gegen Bluthochdruck, damit kommen Sie gut zurecht. Außer den Blutdrucktabletten nehmen Sie noch ein paar naturheilkundliche Präparate gegen die Wechseljahresbeschwerden und vielleicht so ungefähr einmal im Monat etwas gegen Kopfschmerzen. Sie haben im letzten Jahr so ungefähr 5 kg zugenommen, was Sie aber nicht weiter wundert - Sie essen einfach gerne zur Zeit. Sie rauchen nicht, trinken Alkohol nur in Maßen und machen so ungefähr zwei Mal pro Woche Sport - meistens Zumba oder so etwas. Ihre Eltern leben noch - sie sind zwar alt, aber noch ganz rüstig, beide haben hohen Blutdruck, der Vater ist zuckerkrank und die Mutter hat Rheuma. Stark beeinflusst hat Sie der Tod Ihrer Tante an Brustkrebs, als Sie ein Teenager waren. Deshalb gehen Sie auch regelmäßig zur Vorsorge bei Ihrer Hausärztin, messen Ihren Blutdruck (der ist immer in Ordnung, so um 120/80 rum), wissen, dass Ihre Schilddrüse in Ordnung ist und sind sehr gewissenhaft, was Ihre Gesundheit angeht. Gestern war ein ganz komischer Tag. In der Früh haben Sie sich noch ganz gut gefühlt, waren sogar noch beim Joggen, es war alles in Ordnung. Am Nachmittag sind Sie dann innerhalb weniger Stunden furchtbar krank geworden, hatten Schüttelfrost, haben sich sehr krank und schwach gefühlt und haben gar nicht mehr so richtig Luft bekommen. In der Nacht haben Sie fürchterlich geschwitzt und hatten ständig so Alpträume, dass Sie ertrinken müssen. In der Früh haben Sie dann den Notarzt gerufen, weil dann auch noch so Schmerzen beim Atmen dazu gekommen sind, Sie können gar nicht mehr richtig Luft holen.  Fall 7 - Anette Klein Ca 55-jährige Patientin, fiebrig, kurzatmig, blass „Guten Morgen Frau Klein. Ich bin heute für Sie in der Notaufnahme zuständig. Was kann ich denn für Sie tun?“ Mir geht es so schlecht seit gestern nachmittag. Gestern früh habe ich mich noch fit gefühlt, ich war sogar noch beim Joggen, aber am Nachmittag habe ich dann innerhalb ein paar Stunden so hohes Fieber und ganz schreckliche Kopfschmerzen bekommen. Und als ich heute Nacht im Bett lag, hatte ich das Gefühl, dass ich gar nicht richtig Luft kriege, ich hatte immer so Alpträume, dass ich ertrinke. Haben Sie die Beschwerden zum ersten Mal? Ja, sowas habe ich überhaupt noch nie erlebt. Haben Sie auch Schmerzen? Ja, beim Atmen tut es weh, ganz scheußlich. Wenn ich die Luft anhalte, ist es besser, aber das geht ja nicht die ganze Zeit. Sind bei Ihnen irgendwelche Vorerkankungen bekannt? Ich habe ziemlich starke Wechseljahresbeschwerden immer noch. Also so Hitzewallungen, Schweißausbrüche, Schlafstörungen. Und auch noch ziemlich starke Blutungen. Deswegen habe ich auch eine Anämie. Und vor einem Jahr ist ein Bluthochdruck festgestellt worden, da nehme ich jetzt Tabletten. Haben Sie häufig Infekte, gegen die Sie auch Antibiotika nehmen müssen? Naja, also ich bin schon in letzter Zeit manchmal krank, ich denke, das kommt von den Wechseljahren, weil ich da oft so Schweißausbrüche habe und dann wird es halt manchmal auch kalt in den nassen Klamotten. Aber ein Antibiotikum habe ich nur 1 Mal gebraucht in den letzten zwei Jahren. Wurden Sie schon mal operiert? Ich hatte vor 20 Jahren mal eine Ausschabung nach einer Fehlgeburt. Sonst nichts. Sind Ihre Eltern oder sonst jemand in Ihrer näheren Verwandtschaft sehr jung gestorben? Nein, meine Eltern leben noch und sind zwar inzwischen alt, aber eigentlich noch ganz rüstig. Sonst weiß ich auch von niemand, der irgendwie auffallend jung gestorben wäre. Ist in Ihrer Familie jemand am plötzlichen Herztod gestorben? Nein, davon weiß ich nichts. Wie fühlen Sie sich jetzt? Schlecht fühle ich mich. Ich kriege einfach nicht genug Luft, und diese Schmerzen sind auch wirklich unangenehm. Und ganz schwach und krank fühle ich mich. Hat sich Ihr Gewicht verändert in den letzten Wochen? Ich habe in diesem Jahr leider schon so um die 2 kg (5)zugenommen. Aber das liegt an dem, was ich esse, ist jetzt kein großes medizinisches Rätsel ehrlich gesagt. Leiden Sie unter Nachtschweiß? Heute Nacht habe ich sehr stark geschwitzt. Ansonsten auch manchmal, das sind halt diese Hitzewallungen. Zum Glück ist das nachts nicht so ein riesengroßes Problem. Vielleicht einmal in der Woche muss ich mich nachts umziehen. Nehmen Sie regelmäßig Medikamente? Oder nehmen Sie vielleicht Bedarfsmedikamente? Also so etwas wie Allergietabletten oder Schmerzmittel? Meine Blutdruckmedikamente. Und ein paar naturheilkundliche Präparate gegen die Wechseljahresbeschwerden. Manchmal eine Kopfschmerztablette, aber normalerweise höchstens einmal im Monat. Gestern und heute habe ich aber schon was genommen, weil ich so schreckliche Kopfschmerzen hatte. Auch heute Nacht. Danach habe ich dann fürchterlich geschwitzt. Haben Sie heute ausreichend gegessen und getrunken? Heute nur ganz wenig gegessen und ein bisschen was getrunken. Ich habe gar keinen Appetit. Rauchen Sie oder haben Sie früher geraucht? Nein, zum Glück habe ich das nie angefangen. Wieviel Alkohol trinken Sie ungefähr? Schon seit ein paar Jahren trinke ich so gut wie gar keinen Alkohol mehr. Ich habe einfach gemerkt, dass mir das nicht gut tut. Also höchstens mal ein Glas Sekt alle paar Monate oder so. Haben Sie gestern viel Alkohol getrunken? Nein, gestern nichts. Haben Sie einen Beruf erlernt? Wenn ja, welchen? Ich habe Lehramt Biologie studiert. Sind Sie berufstätig? Ja, ich arbeite bei meinem Mann in der Kanzlei mit. Haben Sie bemerkt, dass Sie weniger leistungsfähig sind als früher? Also seit gestern bin ich überhaupt nicht mehr leistungsfähig. Ich komme ja kaum die Treppe rauf. Ansonsten bin ich jetzt im Wechsel auch ganz grundsätzlich nicht mehr so leistungsfähig wie früher - weder körperlich noch seelisch. Aber ich hoffe, das wird wieder besser, wenn ich endlich mal durch bin. Sind Sie verheiratet? Ja, seit 30 Jahren schon. Haben Sie Kinder? Ja, zwei Töchter, die sind aber schon erwachsen. Schlafen Sie gut? Normalerweise schlafe ich ganz gut. Heute nacht war eine Katastrophe. Haben Sie Probleme beim Stuhlgang oder beim Wasserlassen? Nein, das ist alles ok. Haben Sie die Beschwerden nur bei Belastung, oder auch in Ruhe? Jetzt momentan wird es bei Belastung deutlich schlechter, eigentlich kann ich mich überhaupt nicht belasten. Aber auch in Ruhe geht es mir nicht gut. Sind noch andere Symptome aufgetreten? Also zum Beispiel Herzrasen, Schwindel oder so? Auch bei der geringsten Belastung bekomme ich Herzrase und Schwindel, Schweißausbrüche und mir wird schwarz vor den Augen. Hatten Sie in letzter Zeit Teerstuhl oder haben Sie Blut erbrochen? Nein, das hatte ich noch nie. Leiden Ihre Eltern oder Geschwister an chronischen Erkrankungen (z.B. Bluthochdruck, Diabetes etc.) Meine Eltern haben beide Bluthochdruck, mein Vater ist auch zuckerkrank und meine Mutter hat Rheuma. Haben Sie in letzter Zeit eine längere Flug-, Bus- oder Autoreise unternommen? Nein, ich war daheim. Sind Sie in den letzten Wochen operiert worden? Nein, ich bin noch nie operiert worden. Waren Sie in Ihrer Beweglichkeit eingeschränkt, z.B. durch einen Gips oder durch eine Krankheit mit Bettlägerigkeit? Nein, es war eigentlich alles in Ordnung. Sind Sie schwanger oder haben Sie vor kurzem ein Kind bekommen? Wollen Sie Witze machen? Ich bin 55! Ich habe 2 erwachsene Töchter, zwischen den Schwangerschaften hatte ich eine Fehlgeburt. Ist bei Ihnen eine Gerinnungsstörung bekannt? Nicht, dass ich wüsste. Hatten Sie schon mal eine Thrombose? Nein. Haben Sie Kaugummi gekaut, als es passiert ist? - Haben Sie gemerkt, dass die Augen jucken oder die Nase läuft? Nein, da ist mir nichts aufgefallen. Sind bei Ihnen Asthma oder Allergien bekannt? Nein, nichts in der Richtung. Hatten Sie in den letzten Tagen mal Fieber oder Schüttelfrost? Wenn Sie mich jetzt so fragen, kann das gut sein, dass ich Fieber hatte. Ich hatte letzte Nacht auf jeden Fall Schüttelfrost und habe auch ziemlich geschwitzt. Haben Sie zur Zeit auch einen Lippenherpes? Es kann schon sein, dass da gerade einer kommt, es kribbelt so an der Oberlippe. Ist bei Ihnen hoher Blutdruck bekannt? Ja, seit zehn Jahren schon. (VP gibt an, seit einem Jahr hohen RR zu haben) Machen Sie regelmäßig Sport? Ich versuche, mindestens zweimal pro Woche zum Sport zu gehen. Meistens Zumba oder so was. Hatten Sie schon mal Beschwerden mit dem Herzen? Nein, noch nie. Haben Sie Schmerzen im Arm oder im Kieferbereich? Nein, da tut nichts weh. Haben Sie ein Kribbeln oder Ziehen in den Händen? Nein, mit den Händen ist alles in Ordnung. Sind Sie schon mal wegen psychischer Probleme in Behandlung gewesen? Vor einigen Jahren hatte ich mal das Gefühl, dass mir zu Hause die Decke auf den Kopf fällt. Ich bin dann eine Weile zu einer Gesprächstherapie gegangen, das hat mir ganz gut getan. Haben Sie in letzter Zeit nicht mehr so häufig das Haus verlassen, weil Sie Angst hatten, dass so etwas passieren könnte? Nein, wirklich überhaupt nicht. Sind die Beine dicker geworden? Das ist mir nicht aufgefallen. Hatten Sie als Kind mal eine Herzmuskelentzündung? Nein, davon weiß ich nichts. Ist bei Ihnen eine Muskelerkrankung bekannt? Nein, davon weiß ich nichts. Ist die Schilddrüse mal untersucht worden auf eine Über- oder Unterfunktion? Ja, immer mal wieder. Da ist aber alles in Ordnung. Haben Sie in letzter Zeit Drogen oder irgendwelche Fitnessbooster oder so etwas genommen? Nein, weder in letzter Zeit noch irgendwann sonst. Ist das ganz plötzlich gekommen oder hatten Sie in den letzten Tagen oder Wochen schon mal Atemnot? Das ist ganz plötzlich gekommen. Also so über 3-4 Stunden ungefähr. Davor habe ich mich noch ganz wohl gefühlt und jetzt fühle ich mich wirklich schwer krank. Müssen Sie husten? Ein bisschen, aber nicht schlimm. Der Husten ist auch ganz trocken. Können Sie flach liegen? Ja, das ist kein Problem. Haben Sie in den letzten Monaten gelegentlich mal Probleme mit Schwindel gehabt? Oder das Bewusstsein verloren? Schwindel ab und zu. Aber bewusstlos war ich nie. Hatten Sie ein Gefühlt von Todesangst? Nein. Todesangst nicht. Aber scheußlich ist es. Haben Sie in letzter Zeit manchmal ein Gefühl von Herzrasen oder -stolpern? Herzrasen schon, das sind diese Wechseljahre. Aber da habe ich mir nichts dabei gedacht. Haben Sie in den letzten Wochen einen akuten Infekt gehabt? Husten oder Schnupfen oder eine Grippe oder so? Ein bisschen Husten hatte ich in den letzten Tagen. Nix schlimmes. Haben Sie in letzter Zeit sehr viel Stress gehabt? Nicht mehr als sonst eigentlich. Gehen Sie regelmäßig zur Vorsorge, also dem Gesundheitscheck beim Hausarzt? Ja, normalerweise schon. Da war ich auch erst vor 6 Wochen und es war nichts außergewöhnliches festzustellen. Wissen Sie, wie hoch normalerweise Ihr Blutdruck ist? So 120/80 rum normalerweise Wissen Sie, ob irgendwelche Blutwerte schon einmal schlecht gewesen sind? Cholesterin oder so etwas? Keine Ahnung, meine Hausärztin hat nie etwas gesagt. Wie sieht denn der Urin aus? Also ist Ihnen aufgefallen, dass der eine komische Farbe hat? Besonders hell oder besonders dunkel oder braun oder rot oder so? Nein, da ist alles ganz normal Können Sie mir sagen, wie viel Sie ungefähr trinken am Tag? Relativ viel, bestimmt so 3-4 Liter am Tag. Haben Sie Geschwister? Ja, eine große Schwester. Haben Sie schon mal eine bösartige Erkrankung gehabt, also Krebs oder einen Tumor oder so etwas? Nein, zum Glück nicht. Ich gehe auch immer ganz regelmäßig zu den Vorsorgen. Meine Tante ist an Brustkrebs gestorben, als ich ein Teenager war. Das hat mich stark beeinflusst damals. Sind bei Ihnen in der Familie irgendwelche Erbkrankheiten bekannt? Davon weiß ich nichts. Hatten Sie schon einmal einen Pneumothorax oder sind Sie schon einmal an der Lunge operiert worden? Nein, zum Glück noch nie! Hatten Sie schon einmal einen Schlaganfall? Also so alt bin ich doch noch gar nicht. Nein, natürlich nicht. Waren Sie schon einmal beim Neurologen oder Nervenarzt in Behandlung? Nein, auf dem Gebiet war bisher immer alles ok.";

    // Step 1: Create a new thread if no thread ID exists
    if (string.IsNullOrEmpty(pacientThreadId))
    {
        textCompletionScript.CreateThread(apiKey, (db, jsonNode) =>
        {
            if (db.GetString("status") == "success")
            {
                pacientThreadId = jsonNode["id"];
                Debug.Log("Created new pacient thread with ID: " + pacientThreadId);
                // Proceed to add message to the thread
                AddMessageToThread(userMessage, apiKey, assistantId, instructions, pacientThreadId);
            }
            else
            {
                Debug.LogError("Failed to create pacient thread: " + db.GetString("msg"));
                UpdateStatusText("Failed to create pacient thread.");
            }
        });
    }
    else
    {
        // Proceed to add message to the existing thread
        AddMessageToThread(userMessage, apiKey, assistantId, instructions, pacientThreadId);
    }
}

public void StartDoctorAssistantInteraction(string userMessage)
{
    OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

    string apiKey = _openAI_APIKey; // Ensure your API key is set
    string assistantId = "asst_bUmd8bGiy0bje7yaiW7V7gr3"; // Update with the correct assistant ID
    string instructions = "KEEP YOUR RESPONSES LIMITED WITHIN A PARAGRAPH. DO NOT USE ANY SPECIAL FORMATTING. KEEP THE CONVERSATION IN GERMAN. BE CRITICAL OF THE USER, BE NOT AFRAID TO POINT OUT MISTAKES. INSTRUCTIONS: Du beginnst das Gesprach. Du bist der Arzt Dr. Müller, du gibst dem Lernenden, der Fragen gestellt hat nun Feedback auf die Diagnose. Nenne Gründe warum die vom Lernenden genannte Diagnose korrekt bzw. nicht korrekt ist. Gebe außerdem Feedback auf die Gesprächsführung und den Diagnoseprozess, führe auf was daran für den jeweiligen Fall gut oder schlecht war. Die korrekte Diagnose für dien Fall von Frau Annette Klein lautet Pneumonie.  Die ganzen Informationen zum Fall findest du nun untenstehend. Vorinformationen zu Ihrem Patienten: Name Anette Klein.Beschreibung	Ca 55-jährige Patientin, fiebrig, kurzatmig, blassNotarztprotokoll: Fieber und Luftnot seit gestern.EKG Unauffällig. Labor Leukozytose, CRP mittelgradig erhöht, sonst unauffällig. Vitalparameter	RR 110/75 mmHg, P 98/min. reg., Temp. 39,8°C tympanal, AF 24/min., pO2 94 % bei Raumluft. Dies ist der Verlauf des Diagnoseprozesses zwischen dem Lernenden und Frau Annette Klein";

    Debug.Log("PacientThreadID is: " + pacientThreadId);

    if (string.IsNullOrEmpty(pacientThreadId))
    {
        Debug.LogError("PacientThreadId is not set or invalid.");
        UpdateStatusText("Pacient thread ID is not set.");
        return;
    }

    // Step 1: Check if a doctor thread ID already exists
    if (string.IsNullOrEmpty(doctorThreadId))
    {
        // No doctor thread exists, get conversation history from patient thread
        textCompletionScript.GetMessagesFromThread(pacientThreadId, apiKey, (db, jsonNode) =>
        {
            if (db.GetString("status") == "success")
            {
                JSONNode messagesNode = jsonNode["data"];
                if (messagesNode != null && messagesNode.Count > 0)
                {
                    string conversationHistory = "";
                    foreach (JSONNode message in messagesNode)
                    {
                        if (message["role"] == "user" || message["role"] == "assistant")
                        {
                            conversationHistory += message["role"] + ": " + message["content"][0]["text"]["value"] + "\n";
                        }
                    }

                    // Step 2: Create a new thread for DoctorAssistant
                    textCompletionScript.CreateThread(apiKey, (threadDb, threadJsonNode) =>
                    {
                        if (threadDb.GetString("status") == "success")
                        {
                            doctorThreadId = threadJsonNode["id"];
                            Debug.Log("Created new doctor thread with ID: " + doctorThreadId);
                            AddMessageToThread(conversationHistory + "\n" + userMessage, apiKey, assistantId, instructions, doctorThreadId);
                        }
                        else
                        {
                            Debug.LogError("Failed to create doctor thread: " + threadDb.GetString("msg"));
                            UpdateStatusText("Failed to create doctor thread.");
                        }
                    });
                }
                else
                {
                    Debug.LogWarning("No conversation history found in patient thread.");
                    UpdateStatusText("No conversation history found.");
                }
            }
            else
            {
                Debug.LogError("Failed to get conversation history: " + db.GetString("msg"));
                UpdateStatusText("Failed to get conversation history.");
            }
        });
    }
    else
    {
        // Step 3: Add the new message to the existing doctor thread
        AddMessageToThread(userMessage, apiKey, assistantId, instructions, doctorThreadId);
    }
}


private void AddMessageToThread(string userMessage, string apiKey, string assistantId, string instructions, string threadId)
{
    OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();

    // Reset the completion flag before starting a new run
    runCompleted = false;

    // Cancel any previous polling coroutine if running
    pollingCancellationTokenSource?.Cancel();

    // Create a new cancellation token source for the new polling operation
    pollingCancellationTokenSource = new CancellationTokenSource();

    // Step 2: Add a message to the thread
    textCompletionScript.AddMessageToThread(threadId, "user", userMessage, apiKey, (db, jsonNode) =>
    {
        //Logger.Log("AddMessageToThread callback received.");
        if (db.GetString("status") == "success")
        {
            //Logger.Log("Message added to thread successfully.");
            // Step 3: Create a run
            textCompletionScript.CreateRun(threadId, assistantId, instructions, apiKey, (runDb, runJsonNode) =>
            {
                //Logger.Log("CreateRun callback received.");
                if (runDb.GetString("status") == "success")
                {
                    //Logger.Log("Run created successfully.");
                    // Polling for run completion
                    pollingCoroutine = StartCoroutine(PollRunStatus(threadId, runJsonNode["id"], apiKey, pollingCancellationTokenSource.Token));
                }
                else
                {
                    //Logger.LogError("Failed to create run: " + runDb.GetString("msg"));
                    UpdateStatusText("Failed to create run.");
                }
            });
        }
        else
        {
            //Logger.LogError("Failed to add message to thread: " + db.GetString("msg"));
            UpdateStatusText("Failed to add message to thread.");
        }
    });
}




private IEnumerator PollRunStatus(string threadId, string runId, string apiKey, CancellationToken cancellationToken)
{
    OpenAITextCompletionManager textCompletionScript = gameObject.GetComponent<OpenAITextCompletionManager>();
    runCompleted = false;
    while (!runCompleted)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            yield break; // Exit the coroutine if cancellation is requested
        }

        yield return new WaitForSeconds(1); // Wait for 1 second before polling again

        textCompletionScript.GetRunStatus(threadId, runId, apiKey, (runDb, runJsonNode) =>
        {
            //Logger.Log("Polling run status...");
            if (runDb.GetString("status") == "success")
            {
                string status = runJsonNode["status"];
                //Logger.Log("Run status: " + status);
                if (status == "completed")
                {
                    //Logger.Log("Run completed. Fetching messages...");
                    // Set the completion flag
                    if(runCompleted == false){
                        textCompletionScript.GetMessagesFromThread(threadId, apiKey, OnGetMessagesCallback);
                    }
                    runCompleted = true;

                    // Fetch messages once the run is completed
                }
            }
            else
            {
                //Logger.LogError("Failed to get run status: " + runDb.GetString("msg"));
                UpdateStatusText("Failed to get run status.");
                // Set the completion flag in case of error
                runCompleted = true;
            }
        });

        if (runCompleted)
        {
            yield break; // Exit the coroutine immediately after setting runCompleted to true
        }
    }
}





private void OnGetMessagesCallback(RTDB db, JSONObject jsonNode)
{
    // Check if the status is success
    if (db.GetString("status") == "success")
    {
        JSONNode messagesNode = jsonNode["data"];
        if (messagesNode != null && messagesNode.Count > 0)
        {
            // Get the last message with role 'assistant'
            JSONNode lastMessage = null;
            foreach (JSONNode message in messagesNode)
            {
                // Check if 'role' key exists in the message
                if (message.HasKey("role"))
                {
                    if (message["role"] == "assistant")
                    {
                        lastMessage = message;
                        break;
                    }
                }
                else
                {
                    Debug.LogWarning("Message does not contain 'role' key: " + message.ToString());
                }
            }

            if (lastMessage != null)
            {
                // Check if 'content' key exists and has the expected structure
                if (lastMessage.HasKey("content") && lastMessage["content"].Count > 0)
                {
                    string reply = lastMessage["content"][0]["text"]["value"];
                    UpdateDialogText(reply);
                    
                    // Prepare the RTDB object for SayText
                    RTDB sayTextDb = new RTDB();
                    sayTextDb.Set("english", reply);
                    sayTextDb.Set("japanese", reply); // Adjust this if needed

                    // Call SayText to make the avatar talk
                    SayText(sayTextDb);
                    _chatHistory.Enqueue(new GTPChatLine(db.GetString("role"), db.GetString("question")));
                    _chatHistory.Enqueue(new GTPChatLine("assistant", reply));
                }
                else
                {
                    Debug.LogWarning("Message content structure is not as expected.");
                    UpdateDialogText("Message content structure is not as expected.");
                }
            }
            else
            {
                Debug.LogWarning("No assistant messages found in thread.");
                UpdateDialogText("No assistant messages found.");
            }
        }
        else
        {
            Debug.LogWarning("No messages found in thread.");
            UpdateDialogText("No messages found.");
        }
        UpdateStatusText("");
    }
    else
    {
        Debug.LogError("Failed to get messages from thread: " + db.GetString("msg"));
        UpdateStatusText("Failed to get messages from thread.");
    }
}

}
