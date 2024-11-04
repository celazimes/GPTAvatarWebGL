using System.Collections;
using UnityEngine;

public class SecondFriendStartup : MonoBehaviour
{
    private AIManager _aiManager;

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(WaitAndModifyFriend());
    }

    private IEnumerator WaitAndModifyFriend()
    {
        // Wait for 1 second
        yield return new WaitForSeconds(10);

        // Find the AIManager component in the scene
        _aiManager = FindObjectOfType<AIManager>();

        if (_aiManager != null)
        {
            Debug.Log("AIManager found. Modifying friend.");
            _aiManager.ModFriend(1);
        }
        else
        {
            Debug.LogError("AIManager not found in the scene.");
        }
    }

    void PleaseWork(){
        StartCoroutine(WaitAndModifyFriend());
    }

    // Update is called once per frame
    void Update()
    {

    }
}
