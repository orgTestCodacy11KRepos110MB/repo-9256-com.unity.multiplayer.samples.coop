using System;
using Unity.Collections;
using UnityEngine;
using Unity.Networking.Transport;

namespace Unity.BossRoom.ApplicationLifecycle
{
    public class WebsocketClient : MonoBehaviour
    {
        public NetworkDriver m_Driver;
        public NetworkConnection m_Connection;
        public bool m_Done;

        void Start ()
        {
            m_Driver = NetworkDriver.Create(new WebSocketNetworkInterface());
            m_Connection = default(NetworkConnection);
            NetworkEndpoint.TryParse("192.168.0.106", 9000, out var endpoint, NetworkFamily.Ipv4);
            m_Connection = m_Driver.Connect(endpoint);
        }

        public void OnDestroy()
        {
            m_Driver.Dispose();
        }

        void Update()
        {
            m_Driver.ScheduleUpdate().Complete();

            if (!m_Connection.IsCreated)
            {
                if (!m_Done)
                    Debug.Log("Something went wrong during connect");
                return;
            }

            DataStreamReader stream;
            NetworkEvent.Type cmd;

            while ((cmd = m_Connection.PopEvent(m_Driver, out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Connect)
                {
                    Debug.Log("We are now connected to the server");

                    uint value = 1;
                    m_Driver.BeginSend(m_Connection, out var writer);
                    writer.WriteUInt(value);
                    m_Driver.EndSend(writer);
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    uint value = stream.ReadUInt();
                    Debug.Log("Got the value = " + value + " back from the server, disconnecting");
                    m_Done = true;
                    m_Connection.Disconnect(m_Driver);
                    m_Connection = default(NetworkConnection);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client got disconnected from server");
                    m_Connection = default(NetworkConnection);
                }
            }
        }
    }
}
