using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TCPChatServer
{
    public partial class Form1 : Form
    {
        private TcpListener listener;
        private Thread listenThread;
        private bool isRunning;
        private List<TcpClient> connectedClients = new List<TcpClient>();
        private List<string> clientNames = new List<string>();
        private List<string> messageHistory = new List<string>();
        private const string messageHistoryFile = @"C:\Users\LAPTOP T&T\Desktop\DAMH_LTM\Message_History\messageHistory.txt";
        //private const string messageHistoryFile = @"D:\hoc_SOCKET\DAMH_LTM\Message_History\messageHistory.txt";
        public Form1()
        {
            InitializeComponent();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            
            string ipAddress = txtIPAddress.Text; 

            // Lấy cổng từ ô TextBox
            if (int.TryParse(txtPort.Text, out int port))
            {
                try
                {
                    // Nếu địa chỉ IP là "0.0.0.0" hoặc "Any", sử dụng IPAddress.Any
                    if (ipAddress == "0.0.0.0" || ipAddress.ToLower() == "any")
                    {
                        listener = new TcpListener(IPAddress.Any, port);
                    }
                    else
                    {
                        listener = new TcpListener(IPAddress.Parse(ipAddress), port);
                    }

                    listener.Start();
                    isRunning = true;
                    UpdateLog("Server started on " + ipAddress + ":" + port + "...");

                    listenThread = new Thread(ListenForClients);
                    listenThread.Start();
                    btnStart.Enabled = false;
                    btnStop.Enabled = true; // Kích hoạt nút Stop
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error starting server: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Please enter a valid port number.");
            }
        }

        private void ListenForClients()
        {
            while (isRunning)
            {
                TcpClient client = listener.AcceptTcpClient();
                connectedClients.Add(client);
                UpdateLog("Client connected!");

                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
               
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                string clientName = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                clientNames.Add(clientName);
                UpdateLog(clientName + " has joined the chat.");

                LoadAndSendMessageHistory(stream); // Gọi hàm để tải và gửi lịch sử tin nhắn

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string header = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (header.StartsWith("TEXT:"))
                    {
                        string message = header.Substring(5);
                        messageHistory.Add(header); // Lưu tin nhắn vào lịch sử
                        UpdateLog(clientName + ": " + message);
                        BroadcastMessage("TEXT:" + clientName + ": " + message, client);
                        SaveMessageHistory(header); // Lưu tin nhắn vào file
                        ShowNotification("Message Received", $"From {clientName}: {message}");
                    }
                    else if (header.StartsWith("FILE:"))
                    {
                        string[] parts = header.Split(':');
                        string fileName = parts[1];
                        int fileSize = int.Parse(parts[2]);
                        string fileType = parts[3];

                        byte[] fileData = new byte[fileSize];
                        stream.Read(fileData, 0, fileSize);

                        UpdateLog("Received file: " + fileName + " (" + fileType + ")");
                        BroadcastFile(fileName, fileData, fileType);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLog("Client error: " + ex.Message);
            }


            finally
            {
                // Xử lý ngắt kết nối
                connectedClients.Remove(client);
                clientNames.Remove(clientNames.Find(name => name == clientNames[connectedClients.Count])); 
                client.Close();
                UpdateLog("Client disconnected.");
            }
        }

        private void BroadcastMessage(string message, TcpClient senderClient)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            foreach (var client in connectedClients)
            {
                if (client != senderClient && client.Connected)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(messageBytes, 0, messageBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        UpdateLog("Error sending message: " + ex.Message);
                    }
                }
            }
        }

        private void BroadcastFile(string fileName, byte[] fileData, string fileType)
        {
            byte[] fileInfo = Encoding.UTF8.GetBytes("FILE:" + fileName + ":" + fileData.Length + ":" + fileType);

            foreach (TcpClient client in connectedClients)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(fileInfo, 0, fileInfo.Length);
                    stream.Write(fileData, 0, fileData.Length);
                }
                catch (Exception ex)
                {
                    UpdateLog("Error sending file: " + ex.Message);
                }
            }
        }
        

        private void UpdateLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke((MethodInvoker)(() => txtLog.AppendText(message + Environment.NewLine)));
            }
            else
            {
                txtLog.AppendText(message + Environment.NewLine);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopServer();
        }

        private void StopServer()
        {
            isRunning = false;

            if (listener != null)
            {
                try
                {
                    listener.Stop();
                    UpdateLog("Server stopped.");
                }
                catch (Exception ex)
                {
                    UpdateLog("Error stopping server: " + ex.Message);
                }
            }

            foreach (var client in connectedClients)
            {
                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    UpdateLog("Error closing client: " + ex.Message);
                }
            }

            connectedClients.Clear();
            clientNames.Clear(); 
            btnStart.Enabled = true;
            btnStop.Enabled = false; 
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopServer(); 
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadMessageHistory(); 
        }

        private void LoadMessageHistory()
        {
            if (File.Exists(messageHistoryFile))
            {
                string[] messages = File.ReadAllLines(messageHistoryFile);
                foreach (var message in messages)
                {
                    UpdateLog(message); 
                    messageHistory.Add(message); 
                }
            }
        }

        private void SaveMessageHistory(string message)
        {
            File.AppendAllText(messageHistoryFile, message + Environment.NewLine); 
        }

        private void LoadAndSendMessageHistory(NetworkStream stream)
        {
            if (File.Exists(messageHistoryFile))
            {
                string[] messages = File.ReadAllLines(messageHistoryFile);
                foreach (var message in messages)
                {
                    UpdateLog(message); 
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    stream.Write(messageBytes, 0, messageBytes.Length); // Gửi tin nhắn cho client
                }
            }
        }
        private void ShowNotification(string title, string message)
        {
            NotifyIcon notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                BalloonTipTitle = title,
                BalloonTipText = message,
                Visible = true
            };
            notifyIcon.ShowBalloonTip(3000);
            notifyIcon.Dispose();
        }
    }
}