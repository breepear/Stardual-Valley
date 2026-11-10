using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StardewSecondScreen;

internal sealed class Beacon : IDisposable
{
	private const int BeaconPort = 7787;

	private const int IntervalMillis = 3000;

	private readonly int _port;

	private readonly Func<string> _describe;

	private readonly Action<string> _log;

	private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

	public Beacon(int port, Func<string> describe, Action<string> log)
	{
		_port = port;
		_describe = describe;
		_log = log;
	}

	public void Start()
	{
		Task.Run((Func<Task?>)Announce);
	}

	private async Task Announce()
	{
		UdpClient socket = null;
		try
		{
			socket = new UdpClient
			{
				EnableBroadcast = true
			};
			IPEndPoint target = new IPEndPoint(IPAddress.Broadcast, 7787);
			_log($"Announcing on the network every {3} seconds.");
			while (!_cancel.IsCancellationRequested)
			{
				try
				{
					byte[] bytes = Encoding.UTF8.GetBytes(_describe());
					await socket.SendAsync(bytes, bytes.Length, target).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
				}
				try
				{
					await Task.Delay(3000, _cancel.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
					break;
				}
			}
		}
		catch (Exception ex)
		{
			_log("Could not announce on the network: " + ex.Message);
		}
		finally
		{
			socket?.Dispose();
		}
	}

	public void Dispose()
	{
		_cancel.Cancel();
	}
}
