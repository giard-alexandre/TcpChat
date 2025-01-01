using System;
using System.Net.Sockets;
using System.Reactive;
using System.Threading.Tasks;

using ChatApi;
using ChatApi.Messages;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace ChatClient.ViewModels;

public class MainWindowViewModel : ViewModelBase {
	private ChatConnection? _chatConnection;

	public ReactiveCommand<Unit, Unit> Connect { get; private set; }
	public ReactiveCommand<Unit, Unit> Disconnect { get; private set; }
	public ReactiveCommand<string, Unit> Send { get; private set; }
	public ReactiveCommand<string, Unit> Set { get; private set; }

	[Reactive] public string ChatText { get; private set; } = "";

	[Reactive] public string LogText { get; private set; } = "";

	public MainWindowViewModel() {
		Connect = ReactiveCommand.CreateFromTask(ConnectClick);
		Disconnect = ReactiveCommand.Create(DisconnectClick);
		Send = ReactiveCommand.CreateFromTask<string>((s, _) => SendClick(s));
		Set = ReactiveCommand.CreateFromTask<string>((s, _) => SetClick(s));
	}


	private async Task ConnectClick() {
		var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		await clientSocket.ConnectAsync("localhost", 33333);

		LogText += $"Connected to {clientSocket.RemoteEndPoint}\n";

		_chatConnection = new ChatConnection(new PipelineSocket(clientSocket));

		await ProcessSocketAsync(_chatConnection);
	}

	private async Task SendClick(string chatMessage) {
		if (_chatConnection == null) {
			LogText += "No connection!\n";
		}
		else {
			await _chatConnection.SendMessageAsync(new ChatMessage(chatMessage));
			LogText += $"Sent message: {chatMessage}\n";
		}
	}

	private void DisconnectClick() { _chatConnection?.Complete(); }

	private async Task ProcessSocketAsync(ChatConnection chatConnection) {
		try {
			await foreach (var message in chatConnection.InputMessages) {
				if (message is BroadcastMessage broadcastMessage) {
					ChatText += $"{broadcastMessage.From}: {broadcastMessage.Text}\n";
				}
				else
					LogText += $"Got unknown message from {chatConnection.RemoteEndPoint}.\n";
			}

			LogText += $"Connection at {chatConnection.RemoteEndPoint} was disconnected.\n";
		}
		catch (Exception ex) {
			LogText += $"Exception from {chatConnection.RemoteEndPoint}: [{ex.GetType().Name}] {ex.Message}\n";
		}
	}

	private async Task SetClick(string nickname) {
		if (_chatConnection == null) {
			LogText += "No connection!\n";
		}
		else {
			// var nickname = nicknameTextBox.Text;
			try {
				LogText += $"Sending nickname request for {nickname}\n";
				await _chatConnection.SetNicknameAsync(nickname);
				LogText += $"Successfully set nickname to {nickname}\n";
			}
			catch (Exception ex) {
				LogText += $"Unable to set nickname to {nickname}: {ex.Message}\n";
			}
		}
	}
}
