using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AbxClient.Models; // Ensure this matches the namespace of your Packet model

Console.WriteLine("Client App");

await ConnectToServerAsync();

async Task ConnectToServerAsync()
{
    const string serverIp = "127.0.0.1";
    const int serverPort = 3000;
    const int packetSize = 17;

    var ipEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

    try
    {
        using TcpClient client = new();
        client.ReceiveTimeout = 5000; // 5 seconds timeout
        client.SendTimeout = 5000;    // 5 seconds timeout

        await client.ConnectAsync(ipEndPoint);
        using NetworkStream stream = client.GetStream();

        Console.WriteLine("Connected to Server.");

        // Send initial request to stream all packets
        byte[] requestPayload = new byte[] { 1, 0 };
        await stream.WriteAsync(requestPayload, 0, requestPayload.Length);
        Console.WriteLine("Request sent to server.");

        // Receive and process packets
        var packets = await ReceiveDataAsync(stream, packetSize);
        ParsePackets(packets);
        var missingSequences = IdentifyMissingPackets(packets);
        await ResendMissingPacketsAsync(missingSequences, packets, serverIp, serverPort, packetSize);
        SaveDataToJson(packets);
    }
    catch (SocketException)
    {
        Console.WriteLine("Unable to connect to the server. Please ensure the server is active.");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"IO Exception: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex.Message}");
    }
}

async Task<List<Packet>> ReceiveDataAsync(NetworkStream stream, int packetSize)
{
    byte[] buffer = new byte[1024];
    int bytesRead;
    using MemoryStream ms = new();

    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
    {
        ms.Write(buffer, 0, bytesRead);
    }

    byte[] allData = ms.ToArray();
    var packets = new List<Packet>();

    for (int i = 0; i + packetSize <= allData.Length; i += packetSize)
    {
        byte[] packetBytes = allData.Skip(i).Take(packetSize).ToArray();
        var packet = ParsePacket(packetBytes);
        packets.Add(packet);
    }

    return packets;
}

Packet ParsePacket(byte[] packetBytes)
{
    return new Packet
    {
        Symbol = Encoding.ASCII.GetString(packetBytes, 0, 4),
        Side = (char)packetBytes[4],
        Quantity = BitConverter.ToInt32(packetBytes.Skip(5).Take(4).Reverse().ToArray()),
        Price = BitConverter.ToInt32(packetBytes.Skip(9).Take(4).Reverse().ToArray()),
        Seq = BitConverter.ToInt32(packetBytes.Skip(13).Take(4).Reverse().ToArray())
    };
}

void ParsePackets(List<Packet> packets)
{
    foreach (var packet in packets.OrderBy(p => p.Seq))
    {
        Console.WriteLine($"Symbol: {packet.Symbol}, Side: {packet.Side}, Quantity: {packet.Quantity}, Price: {packet.Price}, Seq: {packet.Seq}");
    }
}

List<int> IdentifyMissingPackets(List<Packet> packets)
{
    var sequences = packets.Select(p => p.Seq).OrderBy(seq => seq).ToList();
    var missing = new List<int>();

    for (int i = sequences.First(); i < sequences.Last(); i++)
    {
        if (!sequences.Contains(i))
        {
            missing.Add(i);
        }
    }

    if (missing.Count > 0)
    {
        Console.WriteLine("Missing sequences: " + string.Join(", ", missing));
    }
    else
    {
        Console.WriteLine("No missing packets.");
    }

    return missing;
}

async Task ResendMissingPacketsAsync(List<int> missingSequences, List<Packet> packets, string serverIp, int serverPort, int packetSize)
{
    foreach (var seq in missingSequences)
    {
        try
        {
            using TcpClient client = new();
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            await client.ConnectAsync(serverIp, serverPort);
            using NetworkStream stream = client.GetStream();

            byte[] requestPayload = new byte[] { 2, (byte)seq };
            await stream.WriteAsync(requestPayload, 0, requestPayload.Length);

            byte[] buffer = new byte[packetSize];
            int bytesRead = await stream.ReadAsync(buffer, 0, packetSize);

            if (bytesRead == packetSize)
            {
                var packet = ParsePacket(buffer);
                packets.Add(packet);
                Console.WriteLine($"Resent packet received: Seq {packet.Seq}");
            }
            else
            {
                Console.WriteLine($"Incomplete packet received for Seq {seq}");
            }
        }
        catch (SocketException)
        {
            Console.WriteLine($"Unable to connect to the server to resend packet Seq {seq}. Please ensure the server is active.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"IO Exception while resending packet Seq {seq}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error while resending packet Seq {seq}: {ex.Message}");
        }
    }
}

void SaveDataToJson(List<Packet> packets)
{
    string json = JsonSerializer.Serialize(packets.OrderBy(p => p.Seq), new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText("packets.json", json);
    Console.WriteLine("Packets saved to packets.json");
}
