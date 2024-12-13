using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;

namespace TCPChatClient
{
    public partial class Form1 : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private string lastReceivedFilePath;
        private string clientName;
        private List<string> serverAddresses = new List<string> { "172.20.10.5" };
        //private List<string> serverAddresses = new List<string> { "172.20.10.7" };
        private int currentServerIndex = 0; 
        private int currentPort = 9999; 

        public Form1()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            ConnectToServer();
        }

        private void ConnectToServer()
        {
            try
            {
                string ipAddress = serverAddresses[currentServerIndex]; 
                clientName = txtClientName.Text;

                client = new TcpClient(ipAddress, currentPort);
                stream = client.GetStream();
                UpdateLog("Connected to server as " + clientName + " at " + ipAddress + ":" + currentPort + ".");
                btnConnect.Enabled = false;

                byte[] nameData = Encoding.UTF8.GetBytes(clientName);
                stream.Write(nameData, 0, nameData.Length);

                // Bắt đầu luồng nhận tin nhắn
                Thread receiveThread = new Thread(ReceiveMessages);
                receiveThread.Start();
            }
            catch (Exception ex)
            {
                UpdateLog("Connection error: " + ex.Message);
                AttemptReconnect(); 
            }
        }

        private void AttemptReconnect()
        {
            currentPort++; 
            UpdateLog("Attempting to connect to server at " + serverAddresses[currentServerIndex] + ":" + currentPort);
            ConnectToServer(); 
        }

        private void ReceiveMessages()
        {
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while (true)
                {
                    
                    if (client == null || !client.Connected)
                    {
                        UpdateLog("Disconnected from server.");
                        MessageBox.Show("Disconnected from server.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        AttemptReconnect(); 
                        break; 
                    }

                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        UpdateLog("Disconnected from server.");
                        MessageBox.Show("Disconnected from server.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        AttemptReconnect(); 
                        break; 
                    }

                    string header = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (header.StartsWith("TEXT:"))
                    {
                        string message = header.Substring(5);
                        UpdateLog(message);
                        ShowNotification("New Message", message);
                    }
                    else if (header.StartsWith("FILE:"))
                    {
                        string[] parts = header.Split(':');
                        string fileName = parts[1];
                        int fileSize = int.Parse(parts[2]);
                        string fileType = parts[3];

                        byte[] fileData = new byte[fileSize];
                        int totalBytesRead = 0;
                        while (totalBytesRead < fileSize)
                        {
                            int bytesReadInIteration = stream.Read(fileData, totalBytesRead, fileSize - totalBytesRead);
                            if (bytesReadInIteration == 0) break; 
                            totalBytesRead += bytesReadInIteration;
                        }
                        // Lưu file vào thư mục tạm thời
                        string uniqueFilePath = Path.Combine(Application.StartupPath, "ReceivedFiles", clientName, fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(uniqueFilePath)); // Tạo thư mục nếu chưa có

                        File.WriteAllBytes(uniqueFilePath, fileData);
                        lastReceivedFilePath = uniqueFilePath; // Lưu đường dẫn file nhận được

                        UpdateLog("File received: " + fileName + ". Click 'Open File' to view.");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLog("Error receiving message: " + ex.Message);
                //AttemptReconnect(); 
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (client == null || !client.Connected) return;

            string message = txtMessage.Text;
            if (!string.IsNullOrWhiteSpace(message))
            {
                byte[] data = Encoding.UTF8.GetBytes("TEXT:" + message);
                stream.Write(data, 0, data.Length);
                UpdateLog(clientName + ": " + message);
                txtMessage.Clear();
            }
        }

        private void btnSendFile_Click(object sender, EventArgs e)
        {
            if (client == null || !client.Connected) return;

            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                string filePath = ofd.FileName;
                byte[] fileData = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                string fileType = Path.GetExtension(fileName).TrimStart('.');

                // Gửi thông tin file
                byte[] fileInfo = Encoding.UTF8.GetBytes("FILE:" + fileName + ":" + fileData.Length + ":" + fileType);
                stream.Write(fileInfo, 0, fileInfo.Length);

                // Gửi dữ liệu file
                stream.Write(fileData, 0, fileData.Length);
                UpdateLog("File sent: " + fileName + " (" + fileType + ")");
            }
        }

        private void btnOpenFile_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(lastReceivedFilePath))
            {
                try
                {
                    // Đảm bảo rằng tiến trình khác không truy cập file cùng lúc
                    if (File.Exists(lastReceivedFilePath))
                    {
                        using (FileStream fs = new FileStream(lastReceivedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fs.Close(); // Đảm bảo file có thể được truy cập
                        }
                    }

                    System.Diagnostics.Process.Start(lastReceivedFilePath);
                }
                catch (Exception ex)
                {
                    UpdateLog("Error opening file: " + ex.Message);
                }
            }
            else
            {
                UpdateLog("No file to open.");
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
            Disconnect();
        }

        private void Disconnect()
        {
            if (client != null && client.Connected)
            {
                try
                {
                    client.Close();
                    UpdateLog("Disconnected from server.");
                }
                catch (Exception ex)
                {
                    UpdateLog("Error disconnecting: " + ex.Message);
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