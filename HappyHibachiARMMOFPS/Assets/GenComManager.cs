﻿using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Assets;

public class GenComManager : MonoBehaviour {

    //ip address to connect to
    private static readonly IPAddress IP = IPAddress.Parse("132.160.49.90");
    //port to connect to
    public const int GC_PORT = 1234;
    //size of update packets in bytes
    public const int UPDATE_SIZE = 17;

    //0 index of update array will hold type of communication, rest is guid of interacted object
    private static byte[] update;
    private Guid id;

    private byte[] type;
    private byte[] data;

    private byte[] accepted;

    private LandmarkInfo landmarkInfo;
    private ColloseumInfo colloseumInfo;
    private List<byte> itemInfo;
    private BattleInfo battleInfo;

    private ManualResetEvent processed;

    //connected socket
    private Socket client;

    //call on event where player touches something
    public static void setUpdate(byte type, Guid objID)
    {
        update[0] = type;
        Buffer.BlockCopy(objID.ToByteArray(), 0, update, 1, 16);
    }

    // Use this for initialization
    void Start () {

        try
        {
            //remote endpoint of the server
            IPEndPoint remoteEP = new IPEndPoint(IP, GC_PORT);

            //create TCP socket
            client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            //connect to remote endpoint
            client.Connect(remoteEP);

            Debug.Log("Connect Successful");

            update = new byte[UPDATE_SIZE];
            type = new byte[1];
            accepted = new byte[1];
            landmarkInfo = new LandmarkInfo();
            colloseumInfo = new ColloseumInfo();
            battleInfo = new BattleInfo();
            processed = new ManualResetEvent(false);
            ItemList.populateItemList();

            //Debug.Log("Send Update Successful");

            //start receiving updates from server
            client.BeginReceive(type, 0, 1, 0, new AsyncCallback(updateDriver), null);
            //Debug.Log("Start async successful");
        }
        //catch exception if fail to connect
        catch (Exception e)
        {
            Debug.Log(e.ToString());
            Debug.Log("Connection Failure");
        }
    }

    private void updateDriver(IAsyncResult ar)
    {
        //need to deal with multiple things at once
        //eg a battle request while in a colloseum
        //send some sort of busy signal if cannot accept


        client.EndReceive(ar);

        byte[] infoBuf;
        //size of incoming data in bytes
        byte[] sizebuf = new byte[2];
        short size;

        //INSTEAD OF CATCHING RESPONSE ASYNC AFTER UPDATE, SET TIMEOUT, MAKE IT A TYPE 5 COM
        //ON TIMEOUT SET FLAG TO DISABLE INCOMING TYPE 5 COM
        //ALSO HAVE BATTLE ID SENT BACK AND COMPARE IN CASE OLD TRYS TO INTERFERE WITH A NEW ONE
        //WHILE REQUEST OUT, DISABLE CERTAIN THINGS, SUCH AS SENDING MORE BATTLE REQUESTS
        //ALSO EVENTUALLY DISABLE INCOMING, HAVE IT RESPOND BUSY
        switch (type[0])
        {
            case 0:
                //what will be sent? one thing will be battleID
                //display prompt asking if player accepts battle request
                //if accept remember to set battleID in battlenetmanager before connecting
                //add flags that indicate user not available to battle, respond availability first

                //for now just tell user they have been challenged

                processed.Reset();
                processed.WaitOne();
                break;
            case 1:
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                colloseumInfo.Name = ASCIIEncoding.ASCII.GetString(infoBuf);
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                colloseumInfo.Description = ASCIIEncoding.ASCII.GetString(infoBuf);

                //receive info relevant to colloseum (rankings, etc)
                //for now just name and description

                processed.Reset();
                processed.WaitOne();
                break;
            case 2:
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                landmarkInfo.Name = ASCIIEncoding.ASCII.GetString(infoBuf);
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                landmarkInfo.Description = ASCIIEncoding.ASCII.GetString(infoBuf);
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                //add image stored in infobuf to landMarkInfo when implemented (right now nothing there, size always 0)

                processed.Reset();
                processed.WaitOne();
                break;
            case 3:
                //what is the message being sent from server (special com)
                //implement later
                processed.Reset();
                processed.WaitOne();
                break;
            case 4:
                client.Receive(sizebuf, 0, 2, 0);
                size = BitConverter.ToInt16(sizebuf, 0);
                infoBuf = new byte[size];
                client.Receive(infoBuf, 0, size, 0);
                itemInfo = new List<byte>(infoBuf);

                processed.Reset();
                processed.WaitOne();
                break;
            default:
                Debug.Log("Unexpected type");
                break;
        }

        client.BeginReceive(type, 0, 1, 0, new AsyncCallback(updateDriver), null);
    }



    // Update is called once per frame
    void Update () {

        //need to add mechanism to make sure multiple requests arent sent before finalized with server, primarily for the battle portion

        //when object touched, pack 
        if (update[0] < 255)
        {
            client.Send(update, UPDATE_SIZE, 0);

            if (update[0] == 0)
            {
                Guid battleID = new Guid();
                BattleNetManager.BattleID = battleID;
                client.Send(battleID.ToByteArray());
                client.BeginReceive(accepted, 0, 1, 0, new AsyncCallback(battleAck), null);
            }
        }
        update[0] = 255;

        if(!processed.WaitOne(0))
        {
            //before drawing stuff make sure they are in the expected screen (eg dont draw landmark info over the overworld map if they exit out of it before processed)
            switch (type[0])
            {
                case 0:
                    bool accepted = false;
                    //tell user they have been challenged and ask if accept
                    //store whether they accept or not in accepted var
                    //details of battle will be implemented later

                    if(accepted)
                    {

                    }

                    break;
                case 1:
                    //draw colloseum info to screen, stored in colloseumInfo
                    break;
                case 2:
                    //draw landmark info to screen, stored in landmarkInfo
                    break;
                case 3:
                    //show user special com details
                    //not implemented yet
                    break;
                case 4:
                    //show user items they received, stored in itemInfo (list of byte item ids)
                    //use ItemList.getDestails(byte id) to get an ItemDetails object containing the items name and a path to its image
                    //for now can just put name or something
                    //if itemInfo is empty, display try again later message (can't get items)
                    //place items in users inventory (even if not drawn to screen), can store as a mapping of the items id and the number held or something like that
                    break;
                default:
                    Debug.Log("Unexpected type");
                    break;
            }

            processed.Set();
        }
	}

    private void battleAck(IAsyncResult ar)
    {
        if(accepted[0] == 0)
        {
            //what happens if match declined?
            //prolly just display notice match declined for now
        }
        else
        {
            //change to battle scene
        }
    }

    //contains info needed to populate landmark
    private class LandmarkInfo
    {
        private string description;
        private string name;
        //something to put picture in, for some reason System.Drawing isnt working?

        public string Description
        {
            get
            {
                return description;
            }

            set
            {
                description = value;
            }
        }

        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                name = value;
            }
        }
    }

    //contains info needed to populate colloseum
    private class ColloseumInfo
    {
        private string description;
        private string name;
        //something to put picture in, for some reason System.Drawing isnt working?

        public string Description
        {
            get
            {
                return description;
            }

            set
            {
                description = value;
            }
        }

        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                name = value;
            }
        }
    }

    //contains info needed to populate colloseum
    private class BattleInfo
    {
        //what information should be told to the user when challenged?

    }
}
