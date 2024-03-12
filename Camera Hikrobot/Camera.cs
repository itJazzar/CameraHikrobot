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


namespace Camera_Hikrobot
{
    public class Camera : IDisposable
    {
        public Queue<string> Codes;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private bool _isSingleCamera;
        private Regex codeRegex;
        public string CodeRegexPattern { get; set; }
        public event EventHandler<string> Error;
        public event EventHandler<string> SingleCodeChanged; // CodeRead - одно событие которое возвращает камРезалт. камРезалт хранит массив кодов
        public event EventHandler<string[]> GroupCodeChanged;


        public Camera(bool isSingleCamera)
        {
            Codes = new Queue<string>();
            _isSingleCamera = isSingleCamera;
            CodeRegexPattern = "^01\\d{14}215.{5}\u001d93.{4}$"; //Молоко
            codeRegex = new Regex(CodeRegexPattern);
        }

        public static void SingleCodeChangedHandler(object sender, string code)
        {
            int i  = 1;
            Console.WriteLine($"Получен {i}-й код: {code}\");");
        }

        public static void ErrorHandler(object sender, string code)
        {
            Console.WriteLine($"Error событие");
        }
        public static void GroupCodeChangedHandler(object sender, string code)
        {
            //Console.WriteLine($"Событие CodeChanged: {code}");
        }

        //static void CheckCodes(Camera cam)
        //{
        //    while (true)
        //    {
        //        if (cam.Codes.Count > 0)
        //        {
        //            string code = cam.DequeueCode();
        //            Console.WriteLine($"Проверка кода в очереди: {code}");
        //        }
        //        Task.Delay(100).Wait();
        //    }
        //}

        public void SetCodeRegex(string pattern)
        {
            CodeRegexPattern = pattern;
            codeRegex = new Regex(CodeRegexPattern);
        }
        public bool Connect(string ipAddress, int port, int timeoutMilliseconds)
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                {
                    Console.WriteLine("Камера уже подключена");
                    return true;
                }

                if (port < 0 || port > 65536 || !IPAddress.TryParse(ipAddress, out IPAddress _))
                {
                    Console.WriteLine("Некорректный IP адрес или порт.");
                    return false;
                }

                _tcpClient = new TcpClient();

                var timer = new System.Timers.Timer(timeoutMilliseconds);

                //timer.Elapsed += (sender, e) =>
                //{
                    
                //    HandleError($"Таймаут при подключении к камере - {timeoutMilliseconds / 1000} сек.");
                //};
                //timer.AutoReset = false;
                //timer.Start();

                //_tcpClient.Connect(ipAddress, port);

                //if (_networkStream != null)
                //{
                //    _networkStream.Dispose();
                //}

                //_networkStream = _tcpClient.GetStream();

                //timer.Stop();

                Console.WriteLine("Камера подключена.\n");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при подключении к камере: {ex.Message}.");
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
                    Console.WriteLine("Отключение...");
                }
            }
            catch (Exception ex)
            {
                HandleError($"Ошибка при отключении от камеры: {ex.Message}.");
            }
        }
        private void HandleError(string errorMessage)
        {
            Error?.Invoke(this, errorMessage);
            Console.WriteLine(errorMessage);
        }
        public async Task ReceiveDataAsync()
        {
            byte[] buffer = new byte[1024];
            StringBuilder receivedDataBuffer = new StringBuilder();

            try
            {
                while (true)
                {

                    if (_networkStream == null)
                    {
                        Console.WriteLine("networkStream is null. Соединение разорвано.");
                        break;
                    }

                    int i = 1;
                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        receivedDataBuffer.Append(receivedData);

                        int endIndex = receivedDataBuffer.ToString().IndexOf("___");

                        if (endIndex != -1)
                        {
                            string codesSubstring = receivedDataBuffer.ToString(0, endIndex);
                            string[] codes = codesSubstring.Split(';');

                            if (_isSingleCamera)
                            {
                                foreach (string code in codes)
                                {
                                    Match match = codeRegex.Match(code);
                                    if (match.Success)
                                    {
                                        Codes.Enqueue(code);
                                        //Console.WriteLine($"Получен {i}-й код: {code}");
                                        SingleCodeChanged?.Invoke(this, code);
                                    }
                                    else
                                    {
                                        //Console.WriteLine($"{i}-й код не распознан: Wrong Struct.");
                                    }
                                    i++;
                                }
                            }
                            else
                            {
                                GroupCodeChanged.Invoke(this, codes);
                            }

                            receivedDataBuffer.Remove(0, endIndex + 3);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError($"Ошибка при считывании данных: {ex.Message}");
            }
        }
        
        public string DequeueCode()
        {
            lock (Codes)
            {
                if (Codes.Count > 0)
                {
                    string code = Codes.Dequeue();
                    SingleCodeChanged?.Invoke(this, code);
                    Console.WriteLine($"Код {code} был удален.");
                    return code;
                }
                else
                    return null;
            }
        }
        public enum Status
        {
            Good,
            Bad
        }
        public interface IDisposable
        {
            void Dispose();
        }
        static async Task Main(string[] args)
        {
            Camera cam = new Camera(true);

            cam.SingleCodeChanged += SingleCodeChangedHandler;
            cam.Error += ErrorHandler;

            cam.Connect("12.12.0.10", 2002, 2000);
               

            cam.SetCodeRegex("^01\\d{14}215.{12}\u001d93.{4}$"); //Бутылки воды 
            //Task.Run(() => CheckCodes(cam));

            await cam.ReceiveDataAsync();

            //cam.Dispose();
            Console.ReadKey();
        }
    }
}
