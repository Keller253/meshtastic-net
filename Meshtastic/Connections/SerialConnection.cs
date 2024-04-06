using Google.Protobuf;
using Meshtastic.Data;
using Meshtastic.Protobufs;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection.Metadata;

namespace Meshtastic.Connections;

public class SerialConnection : DeviceConnection
{
    private readonly SerialPort serialPort;
    private SemaphoreSlim serialPortSemaphore = new(1);

    public SerialConnection(ILogger logger, string port, int baudRate = Resources.DEFAULT_BAUD_RATE) : base(logger)
    {
        serialPort = new SerialPort(port, baudRate)
        {
            Handshake = Handshake.None,
        };
    }

    public SerialConnection(ILogger logger,
        string port,
        DeviceStateContainer container,
        bool dtrEnable = true,
        Handshake handshake = Handshake.None,
        int baudRate = Resources.DEFAULT_BAUD_RATE) : base(logger)
    {
        serialPort = new SerialPort(port, baudRate)
        {
            DtrEnable = false,
            Handshake = handshake,
            WriteBufferSize = 8,
        };
        DeviceStateContainer = container;
    }

    public static string[] ListPorts() => SerialPort.GetPortNames();

    public override async Task Monitor()
    {
        var gotSemaphore = false;

        try
        {
            await serialPortSemaphore.WaitAsync(ShowStopper.Token);
            ShowStopper.Token.ThrowIfCancellationRequested();
            gotSemaphore = true;
            Logger.LogDebug("Opening serial port...");
            serialPort.Open();
            while (serialPort.IsOpen)
            {
                if (serialPort.BytesToRead > 0)
                {
                    var line = serialPort.ReadLine();
                    if (line.Contains("INFO  |"))
                        Logger.LogInformation(line);
                    else if (line.Contains("WARN  |"))
                        Logger.LogWarning(line);
                    else if (line.Contains("DEBUG |"))
                        Logger.LogDebug(line);
                    else if (line.Contains("ERROR |"))
                        Logger.LogError(line);
                    else
                        Logger.LogInformation(line);
                }
                // await Task.Delay(10);

            }
            Logger.LogDebug("Disconnected from serial");

        }
        catch (OperationCanceledException)
        {
            // no need to do anything if cancelled
        }
        finally
        {
            if (gotSemaphore)
            {
                serialPortSemaphore.Release();
            }
        }
    }

    protected override async Task<DeviceStateContainer> WriteToRadio(ToRadio packet, Func<FromRadio, DeviceStateContainer, Task<bool>> isComplete)
    {
        try
        {
            ShowStopper.Token.ThrowIfCancellationRequested();
            DeviceStateContainer.AddToRadio(packet);
            var toRadio = PacketFraming.CreatePacket(packet.ToByteArray());
            if (!serialPort.IsOpen)
                serialPort.Open();
            await serialPort.BaseStream.WriteAsync(PacketFraming.SERIAL_PREAMBLE.AsMemory(0, PacketFraming.SERIAL_PREAMBLE.Length));
            await serialPort.BaseStream.WriteAsync(toRadio);
            VerboseLogPacket(packet);
            await ReadFromRadio(isComplete, Resources.DEFAULT_READ_TIMEOUT, true);
        }
        catch (OperationCanceledException)
        {
            // no need to do anything if cancelled
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error writing to radio: {ex}");
        }
                
        return DeviceStateContainer;
    }

    public override async Task Start()
    {
        if (!serialPort.IsOpen)
            serialPort.Open();
        while (!serialPort.IsOpen)
        {
            await Task.Delay(100);
        }

        // Write some garbage to wake the device and force a resync
        var writtenBytes = 0;

        while (writtenBytes < 32)
        {
            serialPort.BaseStream.WriteByte(PacketFraming.PACKET_FRAME_START[1]);
            writtenBytes++;
        }
        await serialPort.BaseStream.FlushAsync();
        await Task.Delay(200);

        await base.Start();
    }
    public override void Disconnect()
    {
        serialPort.Close();
    }

    protected override async Task WriteToRadio(ToRadio packet)
    {
        try
        {            
            DeviceStateContainer.AddToRadio(packet);
            if (!serialPort.IsOpen)
                serialPort.Open();
            var toRadio = PacketFraming.CreatePacket(packet.ToByteArray());
            await serialPort.BaseStream.WriteAsync(toRadio);
            await serialPort.BaseStream.FlushAsync();
            VerboseLogPacket(packet);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning($"Write operation cancelled");
        }
    }

    protected override async Task ReadFromRadio(Func<FromRadio, DeviceStateContainer, Task<bool>> isComplete, int readTimeoutMs = Resources.DEFAULT_READ_TIMEOUT)
    {
        await ReadFromRadio(isComplete, readTimeoutMs, false);
    }

    protected async Task ReadFromRadio(Func<FromRadio, DeviceStateContainer, Task<bool>> isComplete, int readTimeoutMs, bool unSafe = false)
    {
        var gotSemaphore = false;
        try
        {            
            var sw = new Stopwatch();
            sw.Start();
            while (serialPort.IsOpen)
            {
                if (serialPort.BytesToRead == 0)
                {
                    await Task.Delay(10);
                    continue;
                }
                var buffer = new byte[1];
                await serialPort.BaseStream.ReadAsync(buffer);
                if (await ParsePackets(buffer.First(), isComplete))
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation($"Read from radio cancelled");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Exception reading from radio: {ex}");
        }
        finally
        {
            if (gotSemaphore)
            {
                Logger.LogInformation($"Releasing semaphore from read");
                serialPortSemaphore.Release();
            }
        }
    }
}