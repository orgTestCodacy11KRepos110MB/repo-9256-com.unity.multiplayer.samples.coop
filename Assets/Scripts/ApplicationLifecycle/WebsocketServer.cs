using UnityEngine;
using UnityEngine.Assertions;

using Unity.Collections;
using Unity.Networking.Transport;

public class WebsocketServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    private NativeList<NetworkConnection> m_Connections;

    void Start ()
    {
        m_Driver = NetworkDriver.Create(new WebSocketNetworkInterface());
        var reliableSequencedPipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        var endpoint = NetworkEndpoint.AnyIpv4; // The local address to which the client will connect to is 127.0.0.1
        endpoint.Port = 9000;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
        {
            m_Driver.Listen();
            Debug.Log("Websocket listening");
        }

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    public void OnDestroy()
    {
        if (m_Driver.IsCreated)
        {
            m_Driver.Dispose();
            m_Connections.Dispose();
        }
    }

    void Update()
    {
        if (!m_Driver.IsCreated) return;

        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        // for (int i = 0; i < m_Connections.Length; i++)
        // {
        //     if (!m_Connections[i].IsCreated)
        //     {
        //         Debug.Log("removing one connection in cleanup");
        //         m_Connections.RemoveAtSwapBack(i);
        //         --i;
        //     }
        // }

        // AcceptNewConnections

        bool AcceptConnection()
        {
            var connection = m_Driver.Accept();

            if (connection == default)
            {
                return false;
            }

            // InvokeOnTransportEvent(NetcodeNetworkEvent.Connect,
            //     ParseClientId(connection),
            //     default,
            //     Time.realtimeSinceStartup);
            m_Connections.Add(connection);
            Debug.Log("Accepted a websocket connection");
            return true;

        }
        while (AcceptConnection() && m_Driver.IsCreated)
        {
            ;
        }

        // NetworkConnection c;
        // while ((c = m_Driver.Accept()) != default(NetworkConnection))
        // {
        //     m_Connections.Add(c);
        //     Debug.Log("Accepted a websocket connection");
        // }

        DataStreamReader stream;
        // for (int i = 0; i < m_Connections.Length; i++)
        {
            // var conn = m_Connections[i];
            NetworkEvent.Type cmd;
            while ((cmd = m_Driver.PopEvent(out var conn, out stream)) != NetworkEvent.Type.Empty)
            // while ((cmd = m_Driver.PopEventForConnection(conn, out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    uint number = stream.ReadUInt();

                    Debug.Log("Got " + number + " from the Client adding + 2 to it.");
                    number +=2;

                    m_Driver.BeginSend(NetworkPipeline.Null, conn, out var writer);
                    writer.WriteUInt(number);
                    m_Driver.EndSend(writer);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client disconnected from server");
                    conn = default(NetworkConnection);
                }
            }
        }
    }
}
