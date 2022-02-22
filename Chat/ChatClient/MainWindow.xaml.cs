﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ChatClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _clientSocket.ConnectAsync("localhost", 33333);

            Log.Text += $"Connected to {_clientSocket.RemoteEndPoint}\n";

            var pipe = new Pipe();
            _ = PipelineToSocketAsync(pipe.Reader, _clientSocket);

            var message = "Hello, server!";
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var memory = pipe.Writer.GetMemory(messageBytes.Length + 8);
            BinaryPrimitives.WriteUInt32BigEndian(memory.Span, (uint)messageBytes.Length + 4);
            BinaryPrimitives.WriteUInt32BigEndian(memory.Span.Slice(4), 0);
            messageBytes.CopyTo(memory.Span.Slice(8));
            pipe.Writer.Advance(messageBytes.Length + 8);
            await pipe.Writer.FlushAsync();
        }

        private async Task PipelineToSocketAsync(PipeReader pipeReader, Socket socket)
        {
            while (true)
            {
                ReadResult result = await pipeReader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (true)
                {
                    var memory = buffer.First;
                    if (memory.IsEmpty)
                        break;
                    var bytesSent = await socket.SendAsync(memory, SocketFlags.None);
                    buffer = buffer.Slice(bytesSent);
                    if (bytesSent != memory.Length)
                        break;
                }

                pipeReader.AdvanceTo(buffer.Start);

                if (result.IsCompleted)
                {
                    // TODO: handle completion
                    break;
                }
            }
        }

        private Socket? _clientSocket;
    }
}
