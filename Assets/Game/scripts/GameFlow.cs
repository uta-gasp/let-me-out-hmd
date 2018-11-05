﻿using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityStandardAssets.Characters.FirstPerson;
using Valve.VR;

public class GameFlow : NetworkBehaviour
{
    // visible in editor

    public Rect winArea;
    public KeyStatus KeyStatus;
    public Message message;
    public Light[] winLights;

    // readonly

    public bool isFinished { get { return _isFinished; } }

    // internal 

    const float HEALTH_DECREASE_PER_SECOND = 0.2f;

    DebugDesk _debug;               // external
    Sounds _sounds = null;          // internal
    Logger.LogDomain _logGeneral;
    Logger.LogDomain _logErrors;

    bool _isFinished = false;

    // overrides

    public override void OnStartClient()
    {
        _sounds = GetComponent<Sounds>();
    }

    void Awake()
    {
        _debug = FindObjectOfType<DebugDesk>();
        Logger logger = FindObjectOfType<Logger>();
        if (logger)
        {
            _logGeneral = logger.register("game");
            _logErrors = logger.register("error");
        }

        // Application.logMessageReceived += HandleLog;

        if (OpenVR.IsHmdPresent())
        {
            UnityEngine.XR.InputTracking.disablePositionalTracking = true;
        }
        else
        {
            FindObjectOfType<SteamVR_PlayArea>()?.gameObject.SetActive(false);
            FindObjectOfType<SteamVR_Render>()?.gameObject.SetActive(false);
        }

        foreach (Light light in (FindObjectsOfType<Light>() as Light[]).Where(light => light.name.StartsWith("win ")))
        {
            light.enabled = false;
        }

        /*
        // networking
        NetworkManager networkManager = FindObjectOfType<NetworkManager>();

        if (OpenVR.IsHmdPresent())
        {
            networkManager.StartClient();
            FindObjectOfType<NetworkManagerHUD>().showGUI = false;
            _debug.print("client started");
        }
        else if (System.Environment.GetCommandLineArgs().Any(arg => (new string[] { "-s", "--server" }).Any(srv => srv == arg) ))
        {
            networkManager.StartServer();
            FindObjectOfType<NetworkManagerHUD>().showGUI = false;
            _debug.print("server started");
        }
        */
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }

        if (!isServer || _isFinished)
            return;

        // check for win: everybody should be outside

        Transform[] players = FindObjectsOfType<FirstPersonController>().Select(obj => obj.transform).ToArray();
        if (players.Length == 0)
            return;

        bool isAnyoneIsStillInside = false;
        foreach (Transform player in players)
        {
            Vector2 playerPositionOnTheFloor = new Vector2(player.position.x, player.position.z);
            if (!winArea.Contains(playerPositionOnTheFloor))
            {
                isAnyoneIsStillInside = true;
                break;
            }
        }

        if (!isAnyoneIsStillInside)
        {
            _isFinished = true;
            Win();
        }
    }

    // public methods

    public void CaptureKey(Key aKey)
    {
        string keyName = aKey.name;
        string doorName = aKey.door.name;

        // on a server
        OpenDoor(aKey.door);
        UpdateKeyStatus(keyName);

        // on clients
        RpcPlayKeyCaptureSound();
        RpcUpdateKeyStatus(keyName);
        RpcOpenDoor(doorName);
    }

    // server-side
    public void HitPlayer(Transform aPlayer, float aWeight)
    {
        if (_isFinished)
            return;

        PlayerAvatar player = aPlayer.gameObject.GetComponent<PlayerAvatar>();
        if (!player.isAlive)
            return;
        
        player.decreaseHealth(HEALTH_DECREASE_PER_SECOND * Time.deltaTime * aWeight);  

        if (!player.isAlive)
        {
            player.respawn();
        }
    }

    // internal methods

    void OpenDoor(GameObject aDoor)
    {
        aDoor.GetComponent<BoxCollider>().enabled = false;
        aDoor.GetComponent<Animator>().SetBool("open", true);
    }

    void UpdateKeyStatus(string aName)
    {
        KeyStatus.collect(aName);
    }

    /*
    void Lost()
    {
        if (!isServer)
            return;

        // disable players
        PlayerAvatar[] players = FindObjectsOfType<PlayerAvatar>().Where(player => player.isAlive).ToArray();
        foreach (var player in players)
        {
            player.RpcDisable();
        }

        message.show("Game over...");

        RpcEndGame("Lost");
    }*/

    void Win()
    {
        if (!isServer)
            return;

        message.show("Completed!");

        RpcLightsOn();
        RpcEndGame("Win");
    }

    void Exit()
    {
        Application.Quit();
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        {
            if (_logErrors != null)
                _logErrors.add(logString);
            Debug.Log($"ERROR: {logString} [{stackTrace}]");
        }
    }

    [ClientRpc]
    void RpcPlayKeyCaptureSound()
    {
        if (_sounds != null)
            _sounds.getKey();
    }

    [ClientRpc]
    void RpcOpenDoor(string aName)
    {
        if (_logGeneral != null)
            _logGeneral.add($"open-door\t{aName}");

        GameObject door = FindObjectsOfType<GameObject>().Single(obj => obj.tag == "door" && obj.name == aName);
        OpenDoor(door);
    }

    [ClientRpc]
    void RpcUpdateKeyStatus(string aName)
    {
        UpdateKeyStatus(aName);
    }

    [ClientRpc]
    void RpcLightsOn()
    {
        if (_logGeneral != null)
            _logGeneral.add("lights-on");

        foreach (Light light in winLights)
        {
            light.gameObject.SetActive(true);
        }
    }

    [ClientRpc]
    void RpcEndGame(string aResult)
    {
        if (_logGeneral != null)
            _logGeneral.add($"finished\t{aResult}");

        Invoke($"Show{aResult}Message", 2);
        Invoke("Exit", 4);
    }
}