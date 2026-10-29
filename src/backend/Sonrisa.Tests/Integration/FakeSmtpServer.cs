using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Sonrisa.Tests.Integration;

/// <summary>
/// A minimal SMTP server for the tests (no package): accepts a connection, answers EHLO / MAIL / RCPT / DATA / QUIT
/// and keeps the messages it received. <see cref="RcptReply"/> lets a test make the server refuse the recipient.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<ReceivedMail> _mails = [];

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The server's answer to RCPT TO; a 4xx or 5xx refuses the recipient.</summary>
    public string RcptReply { get; set; } = "250 OK";

    public IReadOnlyList<ReceivedMail> Mails
    {
        get
        {
            lock (_mails)
            {
                return _mails.ToList();
            }
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
            // Test is over.
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 fake ESMTP");
                string? to = null;
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var command = line.ToUpperInvariant();
                    if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                    {
                        await writer.WriteLineAsync("250 fake");
                    }
                    else if (command.StartsWith("MAIL FROM"))
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (command.StartsWith("RCPT TO"))
                    {
                        to = line[(line.IndexOf('<') + 1)..line.IndexOf('>')];
                        await writer.WriteLineAsync(RcptReply);
                    }
                    else if (command == "DATA")
                    {
                        await writer.WriteLineAsync("354 go ahead");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync(_stop.Token) is { } dataLine && dataLine != ".")
                        {
                            data.AppendLine(dataLine.StartsWith("..") ? dataLine[1..] : dataLine);
                        }

                        lock (_mails)
                        {
                            _mails.Add(new ReceivedMail(to ?? string.Empty, data.ToString()));
                        }

                        await writer.WriteLineAsync("250 queued");
                    }
                    else if (command == "QUIT")
                    {
                        await writer.WriteLineAsync("221 bye");
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested || !client.Connected)
            {
                // Connection closed by the client or the test is over.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // Already stopped.
        }

        _stop.Dispose();
    }
}

public sealed record ReceivedMail(string To, string Data);
