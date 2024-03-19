using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.CodeDom;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.IO;
using static Camera_Hikrobot.Camera;
using System.Net.NetworkInformation;
using System.Runtime.ConstrainedExecution;


namespace Camera_Hikrobot
{
    public class Camera : Camera.IDisposable
    {

        public Queue<string> Codes;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Regex _codeRegex;
        private string _codeRegexPattern;
        public CameraType CamType { get; set; }
        public bool IsSingleCamera { get; set; }
        public int CodesToRead { get; set; }
        public int Port { get; set; }
        public string IpAddress { get; set; }
        public int ConnectionTimeoutMSec { get; set; }
        public string CodeRegexPattern
        {
            get { return _codeRegexPattern; }
            set
            {
                _codeRegexPattern = value;
                _codeRegex = new Regex(_codeRegexPattern);
            }
        }
        public event EventHandler<string> CodeRead; // CodeRead - одно событие которое возвращает камРезалт. камРезалт хранит массив кодов
        public event EventHandler<CamResult> CodeCamResult;
        public event EventHandler<CamResult> GroupCodeCamResult;
        public event EventHandler<Exception> ConnectionException;

        public Camera(bool isSingleCamera = true, int codesToRead = 1, int port = 2002, string codeRegexPattern = "^01\\d{14}215.{5}\u001d93.{4}$")
        {
            Codes = new Queue<string>();
            Port = port;
            IsSingleCamera = isSingleCamera;
            CodeRegexPattern = codeRegexPattern; //Молоко
            _codeRegex = new Regex(CodeRegexPattern);
            CodesToRead = codesToRead;
        }
        public enum CameraType
        {
            Hikrobot,
            Datalogic
        }
        public async Task<bool> Start()
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                Console.WriteLine("Камера уже подключена");
                return true;
            }

            if (Port < 0 || Port > 65536 || !IPAddress.TryParse(IpAddress, out IPAddress _))
            {
                Console.WriteLine("Некорректный IP адрес или порт.");
                return false;
            }

            try
            {
                _tcpClient = new TcpClient();
                //_tcpClient.SendTimeout = 2000;
                //_tcpClient.ReceiveTimeout = 2000;

                var timeoutToConnect = Task.Delay(ConnectionTimeoutMSec);
                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);

                if (await Task.WhenAny(connectTask, timeoutToConnect) != connectTask)
                {
                    // Таймаут подключения
                    Console.WriteLine($"Таймаут при подключении к камере: {ConnectionTimeoutMSec / 1000} сек.");
                    return false;
                }

                await connectTask;
                Console.WriteLine("Клиент запущен");
                _networkStream = _tcpClient.GetStream();

                if (_tcpClient.Connected)
                {
                    Console.WriteLine($"Подключение с {_tcpClient.Client.RemoteEndPoint} установлено.\n");
                }
                return true;
            }

            catch (SocketException ex)
            {
                ConnectionException.Invoke(this, ex);
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_networkStream != null)
                {
                    _networkStream.Dispose();
                }

                if (_tcpClient != null)
                {
                    _tcpClient.Close();
                    Console.WriteLine("Отключение камеры...");
                }
            }
            catch (Exception ex)
            {
                ConnectionException.Invoke(this, ex);
            }
        }

        public async Task ReceiveDataAsync()
        {
            try
            {
                while (_tcpClient.Connected)
                {
                    byte[] buffer = new byte[1024];
                    StringBuilder receivedDataBuffer = new StringBuilder();

                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length); // Ловить 0, когда поймал - кидать экспешн и закрывать соединение
                    if (bytesRead > 0)
                    {
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        receivedDataBuffer.Append(receivedData);

                        int endIndex = receivedDataBuffer.ToString().IndexOf("___");

                        if (endIndex != -1)
                        {
                            string codesSubstring = receivedDataBuffer.ToString(0, endIndex);
                            string[] codes = codesSubstring.Split(';');

                            if (IsSingleCamera)
                            {
                                CodesToRead = codes.Length;

                                for (int i = 0; i < Math.Min(codes.Length, CodesToRead); i++)
                                {
                                    string code = codes[i];
                                    Match match = _codeRegex.Match(code);

                                    if (match.Success)
                                    {
                                        Codes.Enqueue(code); //?
                                        //CodeRead?.Invoke(this, code);

                                        var camResult = CamResult.Good(code);
                                        camResult.AddCode(code);
                                        CodeCamResult.Invoke(this, camResult);
                                    }
                                    else
                                    {
                                        CodeCamResult.Invoke(this, CamResult.Bad("WRONG STRUCT"));
                                    }
                                }
                            }
                            else
                            {
                                //Логика для групповой камеры
                                var camResult = new CamResult();

                                for (int i = 0; i < Math.Min(codes.Length, CodesToRead); i++)
                                {
                                    string code = codes[i];
                                    Match match = _codeRegex.Match(code);
                                    if (match.Success)
                                    {
                                        camResult.AddCode(code);
                                        //CodeCamResult.Invoke(this, CamResult.Bad());
                                    }
                                    else
                                    {
                                        CodeCamResult.Invoke(this, CamResult.Bad("WRONG STRUCT"));
                                        //Error?.Invoke(this, code);
                                        //CodeCamResult.Invoke(this, CamResult.Bad());
                                    }
                                }

                                try
                                {
                                    // Проверка, были ли успешно считаны все коды
                                    if (camResult.ResultCodes.Count == CodesToRead)
                                    {
                                        // Все коды были успешно считаны
                                        GroupCodeCamResult.Invoke(this, camResult);
                                    }

                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Количество считанных кодов не соответствует заданному {CodesToRead}.\nError: {ex}");
                                }

                                //GroupCodeCamResult.Invoke(this, camResult);

                                for (int i = Math.Min(codes.Length, CodesToRead); i < codes.Length; i++)
                                {
                                    string code = codes[i];
                                    CodeCamResult.Invoke(this, CamResult.Bad("NO READ"));
                                }
                            }

                            receivedDataBuffer.Remove(0, endIndex + 3);
                            _networkStream.Flush();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ConnectionException.Invoke(this, ex);
            }
            finally
            {
                _networkStream?.Dispose();
                _tcpClient?.Close();
            }
        }

        private async Task<bool> KeepAliveTask()
        {
            while (_tcpClient.Connected)
            {
                try
                {
                    var tmp = new byte[1];
                    _tcpClient.Client.Send(tmp, 0, 0);
                    //Console.WriteLine("Keep alive connection is true"); //Отладка
                    //return true;
                }
                catch (SocketException e)
                {
                    if (e.NativeErrorCode.Equals(10035)) // 10035 == WSAEWOULDBLOCK
                    {
                        Console.WriteLine("Keep alive connection is still true");
                    }
                    else
                    {
                        Console.WriteLine($"Error checking camera connection: {e.Message}");
                        return false;
                    }
                }

                await Task.Delay(1000); // Проверка соединения каждую секунду
            }
            Console.WriteLine("Error checking camera connection.");
            return false; // Соединение разорвано
        }

        public void PingCamera(int pingTimes, int timeout, int delayBetweenPings)
        {
            ThreadPool.QueueUserWorkItem((state) =>
            {
                //string to hold our return message
                string returnMessage = string.Empty;

                //set the ping options, TTL 128
                PingOptions pingOptions = new PingOptions(128, true);

                //create a new ping instance
                Ping ping = new Ping();

                //32 byte buffer (create empty)
                byte[] buffer = new byte[32];

                //first make sure we actually have an internet connection
                if (NetworkInterface.GetIsNetworkAvailable())
                {
                    //here we will ping the host pingTimes (standard)
                    for (int i = 0; i < pingTimes; i++)
                    {
                        try
                        {
                            //send the ping pingTimes to the host and record the returned data.
                            //The Send() method expects 4 items:
                            //1) The IPAddress we are pinging
                            //2) The timeout value
                            //3) A buffer (our byte array)
                            //4) PingOptions
                            PingReply pingReply = ping.Send(IpAddress, timeout, buffer, pingOptions);

                            //make sure we dont have a null reply
                            if (!(pingReply == null))
                            {
                                switch (pingReply.Status)
                                {
                                    case IPStatus.Success:
                                        returnMessage = string.Format("Reply from {0}: bytes={1} time={2}ms TTL={3}", pingReply.Address, pingReply.Buffer.Length, pingReply.RoundtripTime, pingReply.Options.Ttl);
                                        Console.WriteLine(returnMessage);
                                        break;
                                    case IPStatus.TimedOut:
                                        returnMessage = "Connection has timed out...";
                                        Console.WriteLine(returnMessage);
                                        break;
                                    default:
                                        returnMessage = string.Format("Ping failed: {0}", pingReply.Status.ToString());
                                        Console.WriteLine(returnMessage);
                                        break;
                                }
                            }
                            else
                            {
                                returnMessage = "Connection failed for an unknown reason...";
                                Console.WriteLine(returnMessage);
                            }
                        }
                        catch (PingException ex)
                        {
                            returnMessage = string.Format("Connection Error: {0}", ex.Message);
                            Console.WriteLine(returnMessage);
                        }
                        catch (SocketException ex)
                        {
                            returnMessage = string.Format("Connection Error: {0}", ex.Message);
                            Console.WriteLine(returnMessage);
                        }

                        // Delay between pings
                        Thread.Sleep(delayBetweenPings);
                    }
                    Console.WriteLine("Ping completed\n");
                }
                else
                {
                    returnMessage = "No Internet connection found...";
                    Console.WriteLine(returnMessage);
                }
            });
        }

        public string DequeueCode()
        {
            lock (Codes)
            {
                if (Codes.Count > 0)
                {
                    string code = Codes.Dequeue();
                    CodeRead?.Invoke(this, code);
                    Console.WriteLine($"Код {code} был удален.");
                    return code;
                }
                else return null;
            }
        }
        public interface IDisposable
        {
            void Dispose();
        }

        public class CamResult : IDisposable
        {
            public List<string> ResultCodes { get; }
            public Status ResultStatus { get; }
            public string Code { get; }
            public enum Status
            {
                Good,
                Bad
            }
            public void Dispose()
            {
                ResultCodes.Clear();
            }
            private CamResult(Status resultStatus, string code)
            {
                ResultStatus = resultStatus;
                Code = code;
                ResultCodes = new List<string>();
            }
            public CamResult()
            {
                ResultCodes = new List<string>();
                ResultStatus = Status.Good;
            }
            public void AddCode(string code)
            {
                ResultCodes.Add(code);
            }
            public static CamResult Good(string code)
            {
                return new CamResult(Status.Good, code); //List писать 
            }
            public static CamResult Bad(string message)
            {
                return new CamResult(Status.Bad, message);
            }
            public override string ToString()
            {
                return $"Status: {ResultStatus}. Codes:\n{string.Join("\n", ResultCodes)}";
            }
        }


        public static void CodeReadHandler(object sender, string code)
        {
            Console.WriteLine($"Получен код: {code}");
        }
        public static void CamExceptionHanlder(object sender, Exception ex)
        {
            Console.WriteLine($"Error captured: {ex}");
        }
        public static void GroupCodeCamResultHandler(object sender, CamResult e)
        {
            Console.WriteLine($"{e.ToString()}");
        }
        public static void CodeCamResultHandler(object sender, CamResult e)
        {
            Console.WriteLine($"Status: {e.ResultStatus}. Code: {e.Code}");
        }


        static async Task Main(string[] args)
        {
            Camera cam = new Camera(); // Конструктор с параметрами по умолчанию

            cam.CamType = CameraType.Hikrobot;
            //cam.CamType = CameraType.Datalogic;

            if (cam.CamType == CameraType.Hikrobot)
                Console.WriteLine("Тип камеры: Hikrobot\n");
            else
                Console.WriteLine("Тип камеры: Datalogic\n");         

            cam.IpAddress = "12.12.0.10";
            cam.CodeRegexPattern = "^01\\d{14}215.{12}\u001d93.{4}$"; //Бутылки воды 
            cam.ConnectionTimeoutMSec = 2000;
            cam.IsSingleCamera = false;
            cam.CodesToRead = 3;
            //cam.Port = 2001;

            if (!cam.IsSingleCamera)
                Console.WriteLine($"Задана групповая камера. Количество кодов для считывания: {cam.CodesToRead}.");
            else
                Console.WriteLine("Задана одиночная камера.");

            cam.CodeRead += CodeReadHandler;
            cam.CodeCamResult += CodeCamResultHandler;
            cam.GroupCodeCamResult += GroupCodeCamResultHandler;
            cam.ConnectionException += CamExceptionHanlder;

            bool isConnected = await cam.Start();

            var keepAliveTask = cam.KeepAliveTask();
            var receiveDataTask = cam.ReceiveDataAsync();

            //cam.PingCamera(10, 500, 1000);

            await Task.WhenAll(keepAliveTask, receiveDataTask);

            if (isConnected)
            {
                await cam.ReceiveDataAsync();
            }

            //cam.Dispose();

            Console.WriteLine("Нажми любую клавишу для выхода.");
            Console.ReadKey();
        }
    }
}
