using System;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace SIA
{
    class TCPServer
    {
        private TcpListener tcpListener;
        private Thread listenThread;

        //public void MessageReceivedEvent(object sender, EventArgs args);
        public event EventHandler MessageReceivedEvent;

        private Boolean ready = true;

        public TCPServer(int port)
        {
            this.tcpListener = new TcpListener(IPAddress.Any, port);
            this.listenThread = new Thread(new ThreadStart(ListenForClients));
            this.listenThread.Start();
        }

        private void ListenForClients()
        {
            this.tcpListener.Start();

            while (ready)
            {
                try
                {
                    //blocks until a client has connected to the server

                    TcpClient client = this.tcpListener.AcceptTcpClient();

                    //create a thread to handle communication 
                    //with connected client
                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
                    clientThread.Start(client);
                }
                catch (Exception e) 
                { }
            }
        }

        public void close()
        {
            ready = false;
      //      listenThread.Abort();
            tcpListener.Stop();
        }

        private void HandleClientComm(object client)
        {
            TcpClient tcpClient = (TcpClient)client;
            NetworkStream clientStream = tcpClient.GetStream();

            byte[] message = new byte[4096];
            int bytesRead;

            while (ready)
            {
                bytesRead = 0;

                try
                {
                    //blocks until a client sends a message
                    bytesRead = clientStream.Read(message, 0, 4096);
                }
                catch
                {
                    //a socket error has occured
                    break;
                }

                if (bytesRead == 0)
                {
                    //the client has disconnected from the server
                    break;
                }

                //message has successfully been received
                ASCIIEncoding encoder = new ASCIIEncoding();
                //System.Diagnostics.Debug.WriteLine(encoder.GetString(message, 0, bytesRead));

                EventHandler newEvent = MessageReceivedEvent;
                MessageEventArgs _arg = new MessageEventArgs(encoder.GetString(message, 0, bytesRead));
                newEvent(this, _arg);
            }

            tcpClient.Close();
        }
    }

    public class MessageEventArgs : EventArgs
    {
        public String message = "";

        public MessageEventArgs(String _message)
        {
            this.message = _message;
        }
    }

}