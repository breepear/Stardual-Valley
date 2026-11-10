using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StardewSecondScreen;

internal sealed class Bridge : IDisposable
{
	private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	private readonly int _port;

	private readonly Action<string> _log;

	private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

	private readonly List<TcpClient> _clients = new List<TcpClient>();

	private readonly object _lock = new object();

	private readonly Dictionary<string, string> _latest = new Dictionary<string, string>();

	private readonly ConcurrentDictionary<TcpClient, ConcurrentQueue<byte[]>> _outbound = new ConcurrentDictionary<TcpClient, ConcurrentQueue<byte[]>>();

	private readonly ConcurrentDictionary<TcpClient, bool> _writing = new ConcurrentDictionary<TcpClient, bool>();

	private const int OutboundLimit = 1024;

	private TcpListener? _listener;

	private readonly bool _remote;

	private int _joins;

	/// <summary>Goes up every time the app connects, including reconnects after it restarts.</summary>
	public int Joins => Volatile.Read(ref _joins);

	public bool HasClients
	{
		get
		{
			lock (_lock)
			{
				return _clients.Count > 0;
			}
		}
	}

	public event Action<string>? MessageReceived;

	public Bridge(int port, Action<string> log, bool remote = false)
	{
		_port = port;
		_log = log;
		_remote = remote;
	}

	public void Start()
	{
		try
		{
			_listener = new TcpListener(_remote ? IPAddress.Any : IPAddress.Loopback, _port);
			_listener.Start();
			_log(_remote ? $"Listening on ws://0.0.0.0:{_port} (this machine and the network)" : $"Listening on ws://127.0.0.1:{_port}");
			Task.Run((Func<Task?>)AcceptLoop);
		}
		catch (Exception ex)
		{
			_log("Could not open the socket: " + ex.Message);
		}
	}

	private async Task AcceptLoop()
	{
		while (!_cancel.IsCancellationRequested && _listener != null)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				break;
			}
			Task.Run(() => Serve(client));
		}
	}

	private async Task Serve(TcpClient client)
	{
		_ = 2;
		try
		{
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			string text = await ReadHandshakeKey(stream).ConfigureAwait(continueOnCapturedContext: false);
			if (text == null)
			{
				client.Close();
				return;
			}
			using SHA1 sha = SHA1.Create();
			string text2 = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(text + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
			string s = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + text2 + "\r\n\r\n";
			byte[] bytes = Encoding.ASCII.GetBytes(s);
			await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(continueOnCapturedContext: false);
			lock (_lock)
			{
				_clients.Add(client);
			}
			Interlocked.Increment(ref _joins);
			_log("wemu connected");
			List<string> list;
			lock (_lock)
			{
				list = new List<string>(_latest.Values);
			}
			foreach (string item in list)
			{
				Send(client, item);
			}
			await DrainUntilClosed(client, stream).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			_log("Handshake failed: " + ex.Message);
		}
		finally
		{
			lock (_lock)
			{
				_clients.Remove(client);
			}
			try
			{
				client.Close();
			}
			catch
			{
			}
		}
	}

	private static async Task<string?> ReadHandshakeKey(NetworkStream stream)
	{
		byte[] buffer = new byte[4096];
		int read = 0;
		while (read < buffer.Length)
		{
			int num = await stream.ReadAsync(buffer, read, buffer.Length - read).ConfigureAwait(continueOnCapturedContext: false);
			if (num <= 0)
			{
				return null;
			}
			read += num;
			string text = Encoding.ASCII.GetString(buffer, 0, read);
			if (!text.Contains("\r\n\r\n"))
			{
				continue;
			}
			string[] array = text.Split(new string[1] { "\r\n" }, StringSplitOptions.None);
			foreach (string text2 in array)
			{
				if (text2.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
				{
					return text2.Substring("Sec-WebSocket-Key:".Length).Trim();
				}
			}
			return null;
		}
		return null;
	}

	private async Task DrainUntilClosed(TcpClient client, NetworkStream stream)
	{
		byte[] header = new byte[8];
		try
		{
			while (client.Connected && !_cancel.IsCancellationRequested && await ReadExactly(stream, header, 2).ConfigureAwait(continueOnCapturedContext: false))
			{
				int opcode = header[0] & 0xF;
				if (opcode == 8)
				{
					break;
				}
				bool masked = (header[1] & 0x80) != 0;
				long length = header[1] & 0x7F;
				switch (length)
				{
				case 126L:
					if (await ReadExactly(stream, header, 2).ConfigureAwait(continueOnCapturedContext: false))
					{
						length = (header[0] << 8) | header[1];
						break;
					}
					goto end_IL_0459;
				case 127L:
					if (await ReadExactly(stream, header, 8).ConfigureAwait(continueOnCapturedContext: false))
					{
						length = 0L;
						for (int i = 0; i < 8; i++)
						{
							length = (length << 8) | header[i];
						}
						break;
					}
					goto end_IL_0459;
				}
				if (length < 0 || length > 65536)
				{
					break;
				}
				byte[] mask = new byte[4];
				bool flag = masked;
				if (flag)
				{
					flag = !(await ReadExactly(stream, mask, 4).ConfigureAwait(continueOnCapturedContext: false));
				}
				if (flag)
				{
					break;
				}
				byte[] payload = new byte[length];
				bool flag2 = length > 0;
				bool flag3 = flag2;
				if (flag3)
				{
					flag3 = !(await ReadExactly(stream, payload, (int)length).ConfigureAwait(continueOnCapturedContext: false));
				}
				if (flag3)
				{
					break;
				}
				if (masked)
				{
					for (int j = 0; j < payload.Length; j++)
					{
						payload[j] ^= mask[j % 4];
					}
				}
				if (opcode == 1 && payload.Length != 0)
				{
					string obj = Encoding.UTF8.GetString(payload);
					try
					{
						MessageReceived?.Invoke(obj);
					}
					catch
					{
					}
				}
				continue;
				end_IL_0459:
				break;
			}
		}
		catch
		{
		}
		_log("wemu disconnected");
	}

	private static async Task<bool> ReadExactly(NetworkStream stream, byte[] buffer, int count)
	{
		int num;
		for (int read = 0; read < count; read += num)
		{
			num = await stream.ReadAsync(buffer, read, count - read).ConfigureAwait(continueOnCapturedContext: false);
			if (num <= 0)
			{
				return false;
			}
		}
		return true;
	}

	public void Send(string payload)
	{
		List<TcpClient> list;
		lock (_lock)
		{
			list = new List<TcpClient>(_clients);
		}
		foreach (TcpClient item in list)
		{
			Send(item, payload);
		}
	}

	public void Broadcast(string kind, string payload)
	{
		List<TcpClient> list;
		lock (_lock)
		{
			_latest[kind] = payload;
			list = new List<TcpClient>(_clients);
		}
		foreach (TcpClient item in list)
		{
			Send(item, payload);
		}
	}

	private void Send(TcpClient client, string payload)
	{
		try
		{
			if (!client.Connected)
			{
				return;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(payload);
			using MemoryStream memoryStream = new MemoryStream(bytes.Length + 10);
			memoryStream.WriteByte(129);
			if (bytes.Length < 126)
			{
				memoryStream.WriteByte((byte)bytes.Length);
			}
			else if (bytes.Length <= 65535)
			{
				memoryStream.WriteByte(126);
				memoryStream.WriteByte((byte)(bytes.Length >> 8));
				memoryStream.WriteByte((byte)(bytes.Length & 0xFF));
			}
			else
			{
				memoryStream.WriteByte(127);
				for (int num = 56; num >= 0; num -= 8)
				{
					memoryStream.WriteByte((byte)((long)bytes.Length >> num));
				}
			}
			memoryStream.Write(bytes, 0, bytes.Length);
			Enqueue(client, memoryStream.ToArray());
		}
		catch
		{
			lock (_lock)
			{
				_clients.Remove(client);
			}
		}
	}

	private void Enqueue(TcpClient client, byte[] frame)
	{
		ConcurrentQueue<byte[]> queue = _outbound.GetOrAdd(client, (TcpClient _) => new ConcurrentQueue<byte[]>());
		while (queue.Count >= 1024)
		{
			queue.TryDequeue(out byte[] _);
		}
		queue.Enqueue(frame);
		if (_writing.TryAdd(client, value: true))
		{
			Task.Run(() => WriteLoop(client, queue));
		}
	}

	private async Task WriteLoop(TcpClient client, ConcurrentQueue<byte[]> queue)
	{
		_ = 1;
		try
		{
			NetworkStream stream = client.GetStream();
			while (client.Connected && !_cancel.IsCancellationRequested)
			{
				if (!queue.TryDequeue(out byte[] result))
				{
					await Task.Delay(15, _cancel.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					await stream.WriteAsync(result, 0, result.Length).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
		finally
		{
			_outbound.TryRemove(client, out ConcurrentQueue<byte[]> _);
			_writing.TryRemove(client, out var _);
		}
	}

	public void Dispose()
	{
		_cancel.Cancel();
		List<TcpClient> list;
		lock (_lock)
		{
			list = new List<TcpClient>(_clients);
			_clients.Clear();
		}
		foreach (TcpClient item in list)
		{
			try
			{
				item.Close();
			}
			catch
			{
			}
		}
		try
		{
			_listener?.Stop();
		}
		catch
		{
		}
	}
}
