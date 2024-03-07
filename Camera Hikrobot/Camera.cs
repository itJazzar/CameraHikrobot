using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.CodeDom;

namespace Camera_Hikrobot
{
    public class Camera
    {
        public Queue<string> Codes;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        public Camera()
        {
            Codes = new Queue<string>();
        }

        static async Task Main(string[] args)
        {
            Camera cam = new Camera();

            cam.Connect("12.12.0.10", 2002, 2000);

            //Task.Run(() => CheckCodes(cam));

            await cam.ReceiveDataAsync();

            cam.Disconnect();
        }
        static void CheckCodes(Camera cam)
        {
            while (true)
            {
                if (cam.Codes.Count > 0)
                {
                    string code = cam.DequeueCode();
                    Console.WriteLine($"Проверка кода в очереди: {code}");
                }
                Task.Delay(100).Wait();
            }
        }
        public bool Connect(string ipAddress, int port, int timeoutMilliseconds)
        {
            try
            {
                _tcpClient = new TcpClient(ipAddress, port);
                _tcpClient.ReceiveTimeout = timeoutMilliseconds;
                //_tcpClient.Connect(ipAddress, port);
                _networkStream = _tcpClient.GetStream();

                Console.WriteLine("Камера подключена.\n");
                return true;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при подключении к камере: {ex.Message}.");
                return false;
            }
        }
        public void Disconnect()
        {
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                Console.WriteLine("Отключение от камеры выполнено.");
                Console.ReadKey();
            }
        }
        public async Task ReceiveDataAsync()
        {
            byte[] buffer = new byte[1024];
            StringBuilder receivedDataBuffer = new StringBuilder();

            while (true)
            {
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

                        Console.WriteLine("//////////Начало считывания//////////\n");

                        foreach (string code in codes)
                        {

                            Codes.Enqueue(code);
                            Console.WriteLine($"Получен код: {code}");
                        }

                        receivedDataBuffer.Remove(0, endIndex + 3);
                        Console.WriteLine("\n//////////Конец считывания//////////\n");
                    }
                }

            }
        }
        public string DequeueCode()
        {
            lock (Codes)
            {
                if (Codes.Count > 0)
                    return Codes.Dequeue();
                else
                    return null;
            }
        }
    }
}
