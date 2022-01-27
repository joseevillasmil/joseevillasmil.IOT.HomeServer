using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using joseevillasmil.IOT.Communication;
using Azure.Data.Tables;
using System.Net;
using System.Timers;

namespace joseevillasmil.IOT.Server
{
    class Program
    {
        const string privateKey = "Your 64 characters IOT KEY";
        const string QueueAccessKey = "Service Bus Queue Shared Key";
        const string storageKey = "Storage Shared Key";
        private static Queue<string> localQueue = new Queue<string>();
        private static Dictionary<string, string> endpoints = new Dictionary<string, string>() {
            {"LuzCuarto", "192.168.1.108"}
        };
        private static Dictionary<string, LanClient> IOTclients = new Dictionary<string, LanClient>();
        // messages
        private static ServiceBusConnectionStringBuilder conStr;
        private static QueueClient client;
        private static bool listenerSet = false;

        static void Main(string[] args)
        {
            foreach (KeyValuePair<string, string> endpoint in endpoints)
            {
                IOTclients.Add(endpoint.Key, new LanClient(endpoint.Value, privateKey));
            }
            Thread[] tasks = { new Thread(StartAzureListener),
                                new Thread(UpdateTokens),
                                new Thread(StartLocalListener),
                                new Thread(ReadLocalQueue)
            };
            foreach (Thread item in tasks)
            {
                item.Start();
            }

            System.Timers.Timer timer1 = new System.Timers.Timer();
            timer1.Interval = 6000000;
            timer1.Enabled = true;
            timer1.Elapsed += CheckAllDevices;
            timer1.Start();

            while (true) {
                System.Threading.Thread.Sleep(1000);
            }
        }

        private static void CheckAllDevices(object sender, ElapsedEventArgs e)
        {
            foreach (KeyValuePair<string, LanClient> item in IOTclients)
            {
                // actualizamos.
                var device = new Objects.Device()
                {
                    PartitionKey = "HOME",
                    RowKey = item.Key,
                    UpdatedAt = DateTime.UtcNow,
                    Status = item.Value.State()
                };
                UpdateStateDevice(device);
            }
        }

        private static void UpdateTokens()
        {
            do
            {
                Thread.Sleep(1800000);

                // renew tokens
                foreach (KeyValuePair<string, LanClient> client in IOTclients)
                {
                    IOTclients[client.Key].updateToken();
                }

                Console.WriteLine("Tokens actualizados.");
                GC.Collect();
            } while (true);
        }
        private static void StartAzureListener()
        {
            Console.WriteLine("Escuchando azure...");
            do
            {
                // cada minuto intentamos conectarnos de nuevo.
                while (!AzureListener())
                {
                    Thread.Sleep(1000);
                }
                Thread.Sleep(60000);
                GC.Collect();

            } while (true);
        }
        private static bool AzureListener()
        {
            try
            {
                conStr = new ServiceBusConnectionStringBuilder(QueueAccessKey);
                client = new QueueClient(conStr, ReceiveMode.ReceiveAndDelete, RetryPolicy.Default);
                var messageHandler = new MessageHandlerOptions(ListenerExceptionHandler)
                {
                    MaxConcurrentCalls = 1,
                    AutoComplete = false,
                };
                client.RegisterMessageHandler(ReceiveMessageFromQ, messageHandler);
                return true;
            }
            catch (Exception exe)
            {
                Console.WriteLine("{0}", exe.Message);
                Console.WriteLine("Error ");
                
            }

            return false;
        }
        private static void UpdateStateDevice(Objects.Device device)
        {
            try
            {
                TableClient stclient = new TableClient(storageKey, "Devices");
                try
                {
                    var _exists = stclient.GetEntity<Objects.Device>(device.PartitionKey, device.RowKey);
                    stclient.UpdateEntity(device, _exists.Value.ETag);
                }
                catch
                {
                    stclient.AddEntity(device);
                }
            }
            catch(Exception e) { 
            }
            
        }
        private static async Task ReceiveMessageFromQ(Message message, CancellationToken token)
        {
            string result = Encoding.UTF8.GetString(message.Body);
            Console.WriteLine("He recibido una orden: {0}", result);
            localQueue.Enqueue(result);
            await Task.CompletedTask;
        }
        private static Task ListenerExceptionHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            Console.WriteLine("{0}", exceptionReceivedEventArgs.Exception);
            return Task.CompletedTask;
        }
        private static void StartLocalListener()
        {
            HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://*:8100/");
            httpListener.Start();
            Console.WriteLine("Escuchando localmente...");
            while(true)
            {
                HttpListenerContext context = httpListener.GetContext();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                string token = request.Headers.Get("Authorization");
                
                string responseString = "{'state': 'error'}";

                string command = "";
                if (request != null)
                {
                    if(!String.IsNullOrEmpty(token))
                    {
                        token = token.Split(" ")[1];
                        if (AuthService.ValidateToken(token))
                        {
                            if (request.QueryString != null)
                            {
                                if (request.QueryString.Count > 0)
                                {
                                    responseString = "{'state': 'ok'}";
                                    command = request.QueryString.Get("command");
                                    var thread = new Thread(() => {
                                        if(!localQueue.Contains(command))
                                        {
                                            localQueue.Enqueue(command);
                                        }
                                        
                                    });
                                    thread.Start();
                                }
                            }
                        }
                    }
                }
                
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
                context = null;
                request = null;
                response = null;
                GC.Collect();
            }

        }
        private static void ReadLocalQueue()
        {
            while (true)
            {
                Thread.Sleep(100);
                if(localQueue.Count > 0)
                {
                    ExecuteCommand(localQueue.Dequeue());
                }
            }
        }
        private static void ExecuteCommand(string command)
        {
            string[] items = command.Split("|");
            bool error = false;
            if (IOTclients.ContainsKey(items[0]))
            {
                if (String.Equals(items[1], "On"))
                {
                    if (!IOTclients[items[0]].TurnOn())
                    {
                        //Console.WriteLine("Error al encender");
                        //Console.WriteLine("actualizando token");
                        //IOTclients[items[0]].updateToken();
                        //if (!IOTclients[items[0]].TurnOn())
                        //{
                        //    Console.WriteLine("Error al encender");
                        //    error = true;
                        //}
                    }
                }

                if (String.Equals(items[1], "Off"))
                {
                    if (!IOTclients[items[0]].TurnOff())
                    {
                        //Console.WriteLine("Error al apagar");
                        //Console.WriteLine("actualizando token");
                        //IOTclients[items[0]].updateToken();
                        //if (!IOTclients[items[0]].TurnOff())
                        //{
                        //    Console.WriteLine("Error al apagar");
                        //    error = true;
                        //}
                    }
                }

                // actualizamos.
                var device = new Objects.Device()
                {
                    PartitionKey = "HOME",
                    RowKey = items[0],
                    UpdatedAt = DateTime.UtcNow,
                    Status = items[1]
                };
                UpdateStateDevice(device);
            }
        }
    
    }
}
