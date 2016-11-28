﻿using UnityEngine;
using System.Collections;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public class BattleNetManager : MonoBehaviour
{
    //RACE CONDITION SOMEWHERE, OR SOMETHING...
    private static Guid testGUID = new Guid("dddddddddddddddddddddddddddddddd");
    private static readonly IPAddress testIP = IPAddress.Parse("10.10.10.103");
    public const int BATTLE_PORT = 2224;
    public const int UPDATE_SIZE = 41;

    private static readonly Spawn[] spawns = new Spawn[2]
    {
        new Spawn(new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0)),
        new Spawn(new Vector3(0, 0, 12), Quaternion.Euler(0, 180, 0))
    };

    private static readonly object flagLock = new object();

    //buffer to receive whether incoming message is a client ack or enemy update
    private byte[] isClient;

    //buffer for receiving which spawn to use
    private byte[] spawn;

    //buffer to receive incoming updates
    private byte[] update;

    private ManualResetEvent updateFin;

    //double check for race conditions on data, ensure everything resynced
    private Socket client;
    private GameObject player;
    private GameObject opponent;
    private PlayerControl controller;
    private EnemyUpdate eUpdate;
    //signal to main thread that an update has been prepared
    private bool receiveUpdate;
    private bool sendUpdate;


    // Use this for initialization
    void Start()
    {
        try
        {
            IPEndPoint remoteEP = new IPEndPoint(testIP, BATTLE_PORT);

            // Create a TCP/IP socket.
            client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Connect to the remote endpoint.
            client.Connect(remoteEP);

            isClient = new byte[1];
            spawn = new byte[1];
            update = new byte[UPDATE_SIZE];

            Debug.Log("Connect Successful");

            player = GameObject.FindGameObjectWithTag("Player");
            opponent = GameObject.FindGameObjectWithTag("Enemy");
            controller = player.GetComponent<PlayerControl>();

            receiveUpdate = false;
            sendUpdate = false;

            updateFin = new ManualResetEvent(false);

            eUpdate = new EnemyUpdate();

            client.Send(testGUID.ToByteArray());

            //gets which spawn to use
            client.Receive(spawn, 1, 0);
            //get player spawn info from index of player given by server
            player.transform.position = spawns[spawn[0]].SpawnPos;
            player.transform.rotation = spawns[spawn[0]].SpawnRot;
            //spawn opponent at the other location
            opponent.transform.position = spawns[(spawn[0] + 1) % 2].SpawnPos;
            opponent.transform.rotation = spawns[(spawn[0] + 1) % 2].SpawnRot;

            

            //Debug.Log("Send GUID Successful");

            //initial update
            client.Send(getUpdate());
            
            //Debug.Log("Send Update Successful");

            client.BeginReceive(isClient, 0, 1, 0, new AsyncCallback(updateDriver), null);
            //Debug.Log("Start async successful");
        }

        catch (Exception e)
        {
            Debug.Log(e.ToString());
            Debug.Log("Connection Failure");
        }
    }


    private void updateDriver(IAsyncResult ar)
    {
        client.EndReceive(ar);

        //Debug.Log(controller.BattleEnd);

        //if says something about not being able to access enemy transform outside main thread, send signal to main thread and update in Update function
        if(!controller.BattleEnd)
        {
            //Debug.Log("begin update");

            Debug.Log(isClient[0]);

            //getClient.WaitOne();
            if (isClient[0] == 1)
            {
                //Debug.Log("ack");
                //getClient.Set();


                //need to delegate to main thread
                lock(flagLock)
                {
                    sendUpdate = true;
                }
                
                
            }
            else
            {
                //Debug.Log("update");
                /*
                State state = new State();
                state.ClientSocket = client;
                state.Update = new byte[UPDATE_SIZE];

                state.Player = player;
                state.Enemy = GameObject.FindGameObjectWithTag("Enemy");
                */

                //MIGHT NEED TO BLOCK ON THESE CALLS UNTIL FINISHED READING (ANY SIMILAR CASE)
                //Debug.Log("What?");

                //SOMETHING WEIRD WITH THE SERVER ON DC (not properly shutting down socket?)

                client.BeginReceive(update, 0, UPDATE_SIZE, 0, new AsyncCallback(unpackUpdate), null);
                
                updateFin.WaitOne();
                updateFin.Reset();

            }
            //recursively check isClient while battle is going
            client.BeginReceive(isClient, 0, 1, 0, new AsyncCallback(updateDriver), null);
            //Debug.Log("end update");
        }
        else
        {
            // Release the socket.
            client.Shutdown(SocketShutdown.Both);
            client.Close();
            Debug.Log("Disconnected");
        }


        
    }



    private void unpackUpdate(IAsyncResult ar)
    {
        //Debug.Log("begin unpack");
        client.EndReceive(ar);
        updateFin.Set();
        

        byte flags = update[0];

        //unpack update into EnemyUpdate object
        eUpdate.BattleEnd = (flags & 1) == 1 ? true : false;
        eUpdate.Win = ((flags >> 1) & 1) == 1 ? true : false;
        eUpdate.Sf = ((flags >> 2) & 1) == 1 ? true : false;
        eUpdate.Hpr = ((flags >> 3) & 1) == 1 ? true : false;
        eUpdate.Mp = ((flags >> 4) & 1) == 1 ? true : false;
        eUpdate.Mso = ((flags >> 5) & 1) == 1 ? true : false;

        eUpdate.XPos = BitConverter.ToSingle(update, 1);
        eUpdate.ZPos = BitConverter.ToSingle(update, 5);
        eUpdate.Rot = BitConverter.ToSingle(update, 9);
        eUpdate.Sfx = BitConverter.ToSingle(update, 13);
        eUpdate.Sfz = BitConverter.ToSingle(update, 17);
        eUpdate.Sfrx = BitConverter.ToSingle(update, 21);
        eUpdate.Sfry = BitConverter.ToSingle(update, 25);
        eUpdate.Sfrz = BitConverter.ToSingle(update, 29);
        eUpdate.Mpx = BitConverter.ToSingle(update, 33);
        eUpdate.Mpy = BitConverter.ToSingle(update, 37);

        //signal to main thread to update opponent
        lock(flagLock)
        {
            receiveUpdate = true;
        }
        
        //Debug.Log("end unpack");
    }



    private void testCallback(IAsyncResult ar)
    {
        Debug.Log("Callback success, damn unity");
    }

    private void Update()
    {
        lock (flagLock)
        {
            //Debug.Log(client.IsBound);
            if (sendUpdate)
            {
                //send current information on player position
                client.Send(getUpdate());
                Debug.Log("Update sent");

                //reset flags
                //might have to do something to make sure it doesnt overwrite flags if set this cycle
                reset();
            }
            if(receiveUpdate)
            {
                //run the stored update on the opponent
                eUpdate.runUpdate(controller, opponent);
                Debug.Log("Update run");
            }
        
            receiveUpdate = false;
            sendUpdate = false;
        }
        
    }

    /*
    private void dataHandle(IAsyncResult ar)
    {
       
        //getClient.Reset();
        client.Receive(isClient, 1, 0);
        dataHandle(client);
        Debug.Log("Receive Successful");
        //getClient.WaitOne();
        client.EndReceive(ar);
        if (isClient[0] == 1)
        {
            Debug.Log("isClient Successful");
            //getClient.Set();
            client.Send(getUpdate());
            reset();
        }
        else
        {
            //State state = new State();
            //state.ClientSocket = client;
            //state.Update = new byte[UPDATE_SIZE];

            //GameObject player = GameObject.FindGameObjectWithTag("Player");
            //PlayerControl controller = player.GetComponent<PlayerControl>();

            //state.Player = player;
            //state.Enemy = GameObject.FindGameObjectWithTag("Enemy");

            //receiveUpdate
            client.BeginReceive(update, 0, UPDATE_SIZE, 0, new AsyncCallback(updateOpponent), update);

        }

    }
    */
    
    private void reset()
    {
        controller.Sf = false;
        controller.Hpr = false;
        controller.Mp = false;
        controller.Mso = false;
    }
    
    /*
    private void updateOpponent()
    {
        //State state = (State)ar.AsyncState;
        //ar.AsyncWaitHandle.WaitOne();
        //state.ClientSocket.EndReceive(ar);

        //getClient.Set();

        unpackUpdate(state);
        //state.Enemy.GetComponent<EnemyUpdate>().runUpdate();
    }

    */

    
    
    public byte[] getUpdate()
    {
        byte[] up = new byte[UPDATE_SIZE];
        //GameObject player = GameObject.FindGameObjectWithTag("Player");
        //PlayerControl controller = player.GetComponent<PlayerControl>();
        float xPos = player.transform.position.x;
        float zPos = player.transform.position.z;
        float rot = player.transform.rotation.eulerAngles.y;

        //Debug.Log(rot);

        //can send less data in certain cases, deal with this later, much later
        up[0] = setFlags();

        BitConverter.GetBytes(xPos).CopyTo(up, 1);
        BitConverter.GetBytes(zPos).CopyTo(up, 5);
        BitConverter.GetBytes(rot).CopyTo(up, 9);

        BitConverter.GetBytes(controller.Sfx).CopyTo(up, 13);
        BitConverter.GetBytes(controller.Sfz).CopyTo(up, 17);
        BitConverter.GetBytes(controller.Sfrx).CopyTo(up, 21);
        BitConverter.GetBytes(controller.Sfrx).CopyTo(up, 25);
        BitConverter.GetBytes(controller.Sfrx).CopyTo(up, 29);

        BitConverter.GetBytes(controller.Mpx).CopyTo(up, 33);
        BitConverter.GetBytes(controller.Mpy).CopyTo(up, 37);

        //Debug.Log(BitConverter.ToSingle(up, 9));

        return up;
    }

    private byte setFlags()
    {
        byte flags = 0;

        if (controller.BattleEnd) flags += 1;
        if (controller.Win) flags += (1 << 1);
        if (controller.Sf) flags += (1 << 2);
        if (controller.Hpr) flags += (1 << 3);
        if (controller.Mp) flags += (1 << 4);
        if (controller.Mso) flags += (1 << 5);

        return flags;
    }
}

/*
internal class State
{
    private GameObject player;
    private GameObject enemy;

    private Socket clientSocket;
    // Receive buffer.
    private byte[] update;

    public Socket ClientSocket
    {
        get
        {
            return clientSocket;
        }

        set
        {
            clientSocket = value;
        }
    }

    public byte[] Update
    {
        get
        {
            return update;
        }

        set
        {
            update = value;
        }
    }

    public GameObject Player
    {
        get
        {
            return player;
        }

        set
        {
            player = value;
        }
    }

    public GameObject Enemy
    {
        get
        {
            return enemy;
        }

        set
        {
            enemy = value;
        }
    }
}
    */

