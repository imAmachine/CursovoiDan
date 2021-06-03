﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;

namespace Server
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //  Кодировка приложения
        private Encoding defaultEncode = Encoding.UTF8;

        private int port = 9009;
        private TcpListener server = null;

        private Task[] tasks = new Task[2]; //  Массив хранения серверных задач
        private CancellationTokenSource cts = null;    //  Токен отмены работы задач чтения и прослушивания подключений

        List<User> usersQueue = new List<User>();
        int i = 1;

        public MainWindow()
        {
            InitializeComponent();

            //  Инициализация сервера
            IPAddress ip = Dns.GetHostEntry(Dns.GetHostName()).AddressList[1];
            IPAddress localIP = IPAddress.Parse(ip.ToString());

            server = new TcpListener(localIP, port);
            cts = new CancellationTokenSource();
        }

        private void BtnStartClick(object sender, RoutedEventArgs e)
        {
            //  Токен для завершения серверных задач
            cts = new CancellationTokenSource();

            //  Старт прослушивания порта
            server.Start();

            //  Запуск потоков сервера
            tasks[0] = Task.Factory.StartNew(ServerStart, cts.Token);
            tasks[1] = Task.Factory.StartNew(ServerRead, cts.Token);
        }

        private void ServerRead()
        {
            while (true)
            {
                //  В случае, если поступил запрос на остановку задачи, выполнить выход из задачи
                if (cts.Token.IsCancellationRequested)
                {
                    Dispatcher.Invoke((ThreadStart)delegate { tb_log.Text += "\nОтключение прослушивания новых сообщений от клиентов..."; });
                    return;
                }

                if (usersQueue.Count > 0)
                {
                    usersQueue.AsParallel().ForAll(el =>
                    {
                        NetworkStream ns = el.client.GetStream();
                        StringBuilder resultMessage = new StringBuilder();
                        string msg_str = string.Empty;
                        if (ns.DataAvailable)
                        {
                            
                            byte[] buff = null;
                            while (ns.DataAvailable && ns.CanRead)
                            {
                                buff = new byte[64];
                                ns.Read(buff, 0, buff.Length);
                                resultMessage.AppendFormat("{0}", defaultEncode.GetString(buff));
                            }
                            msg_str = resultMessage.ToString();
                            Dispatcher.Invoke((ThreadStart)delegate { tb_log.Text += msg_str; });

                            
                            if (el.Auth)
                            {
                                /*
                                 Отправка авторизованному юзеру список продуктов
                                 */

                                //Получение потока данных получаемых от клиента
                                NetworkStream stream = el.client.GetStream();

                                //Генерация строки для клиента
                                string response = GetXml("products.xml");
                                byte[] data = defaultEncode.GetBytes(response);

                                //Отправляет данные по потоку на клиент
                                stream.Write(data, 0, data.Length);

                                ServerSend(msg_str, el);
                            }
                            else
                            {
                                /*
                                 Проверка на сууществование юзера в базе
                                 */
                                XmlDocument xDoc = new XmlDocument();
                                xDoc.LoadXml(resultMessage.ToString());
                                XmlElement root = xDoc.DocumentElement;

                                string login = root.ChildNodes[0].Value;
                                string password = root.ChildNodes[1].Value;

                                List<Client> clients = (List<Client>)DW.Deserialize<Client>($@"{AppDomain.CurrentDomain.BaseDirectory}/xmls/clients.xml");
                                if (clients.Where(cl => cl.Login == login && cl.Password == password).Count() > 0)
                                {
                                    el.Auth = true;
                                }

                            }
                        }
                    });
                }
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Отправляет сообщение с сервера
        /// </summary>
        /// <param name="msg">Сообщение, которое передаётся</param>
        private void ServerSend(string msg, User user)
        {
            byte[] buff = defaultEncode.GetBytes(msg);
            NetworkStream ns = user.client.GetStream();
            ns.Write(buff, 0, buff.Length);
        }

        /// <summary>
        /// Запускает сервер и прослушивает сокет на подключение
        /// </summary>
        private void ServerStart()
        {
            Dispatcher.Invoke((ThreadStart)delegate { tb_log.Text = "Сервер запущен. Ожидание подключений..."; });
            while (true)
            {
                //  В случае, если поступил запрос на остановку задачи, выполнить выход из задачи
                if (cts.Token.IsCancellationRequested)
                {
                    Dispatcher.Invoke((ThreadStart)delegate { tb_log.Text += "\nОтключение прослушивания подключений..."; });
                    return;
                }

                //Ожидание нового клиента
                if (server.Pending())
                {
                    //Добавление нового клиента
                    TcpClient client = server.AcceptTcpClient();
                    usersQueue.Add(new User(false, i, client));
                    ++i;

                    Dispatcher.Invoke((ThreadStart)delegate { tb_log.Text += $"\nПодключен клиент ({client.Client.RemoteEndPoint.ToString()}). Выполнение запроса..."; });
                    Dispatcher.Invoke((ThreadStart)delegate { lb_users.Items.Add(client.Client.RemoteEndPoint.ToString()); });
                }
                Thread.Sleep(10);
            }
        }

        private string GetXml(string file)
        {
            using (StreamReader sr = new StreamReader($@"{AppDomain.CurrentDomain.BaseDirectory}/xmls/{file}"))
            {
                return sr.ReadToEnd();
            }
        }

        private void BtnStopClick(object sender, RoutedEventArgs e)
        {
            cts.Cancel();
            server.Stop();
        }
    }
}
