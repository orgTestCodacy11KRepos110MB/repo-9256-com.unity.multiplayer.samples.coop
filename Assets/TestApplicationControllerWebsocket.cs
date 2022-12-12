using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Unity.BossRoom.ApplicationLifecycle;
using UnityEngine;

namespace Unity.Multiplayer.Samples.BossRoom
{
    public class TestApplicationControllerWebsocket : MonoBehaviour
    {
        [SerializeField] private TextAsset m_WebsocketHtmlPage;
        // Start is called before the first frame update
        void Start()
        {

            gameObject.AddComponent<WebsocketServer>();
            gameObject.AddComponent<WebsocketClient>();

            StartHTTPServerAsync();

        }

        async void StartHTTPServerAsync()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:8000/");
            listener.Start();

            while (true)
            {
                Debug.Log("waiting for request");
                // Note: The GetContext method blocks while waiting for a request.
                HttpListenerContext context = await listener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                Debug.Log("Got request");
                // Obtain a response object.

                void WriteResponse(string responseString)
                {
                    HttpListenerResponse response = context.Response;

                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                    // Get a response stream and write the response to it.
                    response.ContentLength64 = buffer.Length;
                    System.IO.Stream output = response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);
                    // You must close the output stream.
                    output.Close();
                }

                void WriteSSEFirstResponse()
                {
                    HttpListenerResponse response = context.Response;
                    response.AddHeader("Content-Type", "text/event-stream");
                    response.AddHeader("Cache-Control", "no-cache");
                    // byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Content-Type: text/event-stream\nCache-Control: no-cache\n\n");
                    // response.OutputStream.Write(buffer);
                    // response.ContentLength64 = 0;
                    response.OutputStream.Flush();
                    WriteMultipleEventsAsync(response);
                }

                async void WriteMultipleEventsAsync(HttpListenerResponse response)
                {
                    System.IO.Stream output = response.OutputStream;

                    const string SSEPrefix = "data: "; // This is important for the SSE protocol
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(1000);
                        string responseString = $"{SSEPrefix}Time: {Time.time}\n\n";

                        Debug.Log("sending response " + responseString);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                        output.Write(buffer, 0, buffer.Length);
                        output.Flush();
                    }
                    output.Close();
                }

                var segments = request.Url.Segments;
                if (segments.Length <= 1)
                {
                    string responseString = m_WebsocketHtmlPage.text;
                    WriteResponse(responseString);
                }
                else if (segments[1] == "SSE")
                {
                    Debug.Log("got SSE first response");
                    WriteSSEFirstResponse();
                }
                else if (segments[1] == "status")
                {
                    string statusResponse = Time.time.ToString();
                    WriteResponse(statusResponse);
                }
                else
                {
                    string ArrayToString<T>(T[] a)
                    {
                        string toPrint = "";
                        foreach (var elem in a)
                        {
                            toPrint += elem.ToString() + "@";
                        }
                        return toPrint;
                    }
                    //throw new Exception("unhandled URI " + ArrayToString<string>(request.Url.Segments));
                }
            }
        }


        // Update is called once per frame
        void Update()
        {

        }
    }
}
