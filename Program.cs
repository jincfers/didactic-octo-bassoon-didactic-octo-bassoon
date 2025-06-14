using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

class Program
{
    // [DllImport("user32.dll")]
    // public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    // [DllImport("user32.dll")]
    // public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint VK_T = 0x54;

    static bool terminate = false;
    static long startTimeL = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    static long lastCountTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    static int frameCount = 0;
    // static InputSimulator sim = new InputSimulator();
    static HttpClient client = new HttpClient();

    [STAThread]
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Control Module... Press CTRL+C or CTRL+ALT+T to terminate.");

        await Task.Run(streamLoop);

        Console.WriteLine("Terminated control module.");
    }

    private static ConcurrentQueue<(byte[] buffer, long timestamp)> frameQueue = new();


    static async Task streamLoop()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("ws://localhost:3000"), CancellationToken.None);
        // await socket.ConnectAsync(new Uri("wss://www.da-rat.free.nf"), CancellationToken.None);
        var introMessage = new
        {
            type = "registerc",
            id = 901235
        };
        string initJson = JsonSerializer.Serialize(introMessage);
        await socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(initJson)),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );


        Console.WriteLine("Streaming!");

        GetScreenResolution(out int width, out int height);
        Console.WriteLine("Width" + width);
        Console.WriteLine("Height" + height);
        int frameSize = width * height * 3;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-f x11grab -framerate 20 -video_size {width}x{height} -i {Environment.GetEnvironmentVariable("DISPLAY")} -probesize 10M -fflags nobuffer -flags low_delay -an -pix_fmt rgb24 -f rawvideo -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine("FFmpeg: " + e.Data);
        };
        process.Start();
        process.BeginErrorReadLine();
        // Stream output = process.StandardOutput.BaseStream;

        // var i = 0;

        for (int i = 0; i < 3; i++)
        // while (true)
        {
            try
            {
                // i++;
                byte[] buffer = new byte[frameSize];

                long captureTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                Stream output = process.StandardOutput.BaseStream;
                int totalRead = 0;

                while (totalRead < frameSize)
                {
                    int read = await output.ReadAsync(buffer, totalRead, frameSize - totalRead);
                    if (read <= 0)
                    {
                        Thread.Sleep(10); // Wait for FFmpeg to produce more data
                        continue;
                    }

                    totalRead += read;
                }

                frameQueue.Enqueue((buffer.ToArray(), captureTime));
                while (frameQueue.Count > 1 && frameQueue.TryDequeue(out _)) ;

                if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - captureTime > 5000)
                {
                    Console.WriteLine($"Dropped frame {i + 1} due to it being too old ({(DateTimeOffset.Now.ToUnixTimeMilliseconds() - captureTime)}ms)");
                    continue;
                }

                using var image = Image.LoadPixelData<Rgb24>(buffer, width, height);
                using var ms = new MemoryStream();
                image.Save($"frames/frame_{i}.jpg");
                // image.SaveAsJpeg(ms, new JpegEncoder { Quality = 35 });

                byte[] imageBytes = ms.ToArray();

                File.WriteAllBytes($"frames/bytes_{i}.txt", buffer);

                var message = new
                {
                    type = "image",
                    id = 901235,
                    img = Convert.ToBase64String(imageBytes),
                    time = captureTime
                };

                string json = JsonSerializer.Serialize(message);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                bool waiting = true;
                var messageBuffer = new byte[1024];

                // while (waiting)
                // {
                //     var result = await socket.ReceiveAsync(new ArraySegment<byte>(messageBuffer), CancellationToken.None);
                //     var fMessage = Encoding.UTF8.GetString(messageBuffer, 0, result.Count);

                //     if (fMessage == "frame")
                //     {
                //         waiting = !waiting;
                //     }
                // }

                await socket.SendAsync(
                    new ArraySegment<byte>(jsonBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
                Console.WriteLine($"Taken frame {i + 1}, which is {(DateTimeOffset.Now.ToUnixTimeMilliseconds() - captureTime)}ms old");

                buffer = new byte[frameSize];
            }
            catch (Exception ex)
            {
                Console.WriteLine("Streaming error: " + ex);
            }
        }
        process.Kill();
        process.Dispose();
    }

    // static async Task Listener(ClientWebSocket socket)
    // {
    //     var buffer = new byte[1024];

    //     while (socket.State == WebSocketState.Open)
    //     {
    //         var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    //         var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

    //         switch (message)
    //         {
    //             case "frame":

    //                 break;

    //             default:
    //                 Console.WriteLine("Unhandled message: " + message);
    //                 break;
    //         }
    //     }
    // }


    // static void ControlLoop()
    // {
    //     while (!terminate)
    //     {
    //         if (FpsLimit(10000))
    //         {
    //             FpsCount();
    //             try
    //             {
    //                 var response = client.GetStringAsync("https://www.da-rat.free.nf/cApi/sqlData/901235")
    //                                      .GetAwaiter().GetResult();
    //                 // var response = client.GetStringAsync("http://localhost:3000/cApi/sqlData/901235")
    //                 //                      .GetAwaiter().GetResult();

    //                 using JsonDocument doc = JsonDocument.Parse(response);
    //                 var sqlData = doc.RootElement.GetProperty("sqlData")[0];

    //                 string[] activeKeys = JsonSerializer.Deserialize<string[]>(sqlData.GetProperty("activeKeys").GetString() ?? "[]");

    //                 List<string> activeKeysList = [.. activeKeys];

    //                 foreach (var key in activeKeysList)
    //                 {
    //                     if (string.IsNullOrWhiteSpace(key)) continue;

    //                     bool handled = false;
    //                     string keyLower = key.Trim().ToLowerInvariant();

    //                     try
    //                     {
    //                         // Map known key names to VirtualKeyCodes
    //                         switch (keyLower)
    //                         {
    //                             case "enter":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
    //                                 handled = true;
    //                                 break;
    //                             case "space":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.SPACE);
    //                                 handled = true;
    //                                 break;
    //                             case "backspace":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.BACK);
    //                                 handled = true;
    //                                 break;
    //                             case "tab":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.TAB);
    //                                 handled = true;
    //                                 break;
    //                             case "esc":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
    //                                 handled = true;
    //                                 break;
    //                             case "escape":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
    //                                 handled = true;
    //                                 break;
    //                             case "left":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.LEFT);
    //                                 handled = true;
    //                                 break;
    //                             case "right":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.RIGHT);
    //                                 handled = true;
    //                                 break;
    //                             case "up":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.UP);
    //                                 handled = true;
    //                                 break;
    //                             case "down":
    //                                 sim.Keyboard.KeyPress(VirtualKeyCode.DOWN);
    //                                 handled = true;
    //                                 break;
    //                             default:
    //                                 // F1–F24 support
    //                                 if (keyLower.StartsWith("f") && int.TryParse(keyLower.Substring(1), out int fkey) && fkey >= 1 && fkey <= 24)
    //                                 {
    //                                     sim.Keyboard.KeyPress((VirtualKeyCode)((int)VirtualKeyCode.F1 + (fkey - 1)));
    //                                     handled = true;
    //                                 }
    //                                 break;
    //                         }

    //                         if (!handled)
    //                         {
    //                             // Assume it's a normal printable character (e.g., "a", "Z", "#", "3")
    //                             sim.Keyboard.TextEntry(key);
    //                         }

    //                         Console.WriteLine($"Typed: {key}");
    //                         activeKeysList.Remove(key);
    //                         Thread.Sleep(10);
    //                     }
    //                     catch (Exception ex)
    //                     {
    //                         Console.WriteLine($"Failed to type {key}: {ex.Message}");
    //                     }
    //                 }

    //                 double mouseX = sqlData.TryGetProperty("mouseX", out var mx) && mx.ValueKind != JsonValueKind.Null ? mx.GetDouble() : 0;
    //                 double mouseY = sqlData.TryGetProperty("mouseY", out var my) && my.ValueKind != JsonValueKind.Null ? my.GetDouble() : 0;
    //                 int mouseS = sqlData.TryGetProperty("mouseS", out var ms) && ms.ValueKind != JsonValueKind.Null ? ms.GetInt32() : 0;
    //                 int mouseLD = sqlData.TryGetProperty("mouseLD", out var mld) && mld.ValueKind != JsonValueKind.Null ? mld.GetInt32() : 0;
    //                 int mouseLU = sqlData.TryGetProperty("mouseLU", out var mlu) && mlu.ValueKind != JsonValueKind.Null ? mlu.GetInt32() : mouseLD;
    //                 int mouseRD = sqlData.TryGetProperty("mouseRD", out var mrd) && mrd.ValueKind != JsonValueKind.Null ? mrd.GetInt32() : 0;
    //                 int mouseRU = sqlData.TryGetProperty("mouseRU", out var mru) && mru.ValueKind != JsonValueKind.Null ? mru.GetInt32() : mouseRD;
    //                 // Console.WriteLine($"{mouseLU} {mouseRU}");

    //                 while (mouseLD > 0 || mouseLU > 0)
    //                 {
    //                     if (mouseLD > 0)
    //                     {
    //                         sim.Mouse.LeftButtonDown();
    //                         Console.WriteLine($"Left Down at {(int)mouseX} {(int)mouseY}");
    //                         mouseLD--;
    //                     }
    //                     if (mouseLU > 0)
    //                     {
    //                         sim.Mouse.LeftButtonUp();
    //                         Console.WriteLine($"Left Up at {(int)mouseX} {(int)mouseY}");
    //                         mouseLU--;
    //                     }
    //                 }

    //                 while (mouseRD > 0 || mouseRU > 0)
    //                 {
    //                     if (mouseRD > 0)
    //                     {
    //                         sim.Mouse.RightButtonDown();
    //                         Console.WriteLine($"Right Down at {(int)mouseX} {(int)mouseY}");
    //                         mouseRD--;
    //                     }
    //                     if (mouseRU > 0)
    //                     {
    //                         sim.Mouse.RightButtonUp();
    //                         Console.WriteLine($"Right Up at {(int)mouseX} {(int)mouseY}");
    //                         mouseRU--;
    //                     }
    //                 }
    //                 if (mouseS > 0)
    //                 {
    //                     if (mouseS == 1)
    //                     {
    //                         sim.Mouse.MiddleButtonDown();
    //                         Thread.Sleep(50);
    //                         sim.Mouse.MiddleButtonUp();
    //                         Console.WriteLine($"Middle click at {(int)mouseX} {(int)mouseY}");
    //                     }
    //                     else if (mouseS == 2)
    //                     {
    //                         sim.Mouse.VerticalScroll(-1);
    //                         Console.WriteLine($"Middle scroll down at {(int)mouseX} {(int)mouseY}");
    //                     }
    //                     else if (mouseS == 3)
    //                     {
    //                         sim.Mouse.VerticalScroll(1);
    //                         Console.WriteLine($"Middle scroll up at {(int)mouseX} {(int)mouseY}");
    //                     }
    //                 }

    //                 sim.Mouse.MoveMouseToPositionOnVirtualDesktop((int)mouseX, (int)mouseY);

    //                 // After executing clicks and keys:
    //                 var payload = new
    //                 {
    //                     sendScreenX = 65535,
    //                     sendScreenY = 65535,
    //                     mouseS = 0,
    //                     activeKeys = activeKeysList,
    //                     MouseLD = mouseLD,
    //                     MouseLU = mouseLU,
    //                     MouseRD = mouseRD,
    //                     MouseRU = mouseRU,
    //                     ID = "901235"
    //                 };

    //                 var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    //                 var response2 = client.PostAsync("https://www.da-rat.free.nf/cApi/sqlData/901235", json).GetAwaiter().GetResult();
    //                 // var response2 = client.PostAsync("http://localhost:3000/cApi/sqlData/901235", json).GetAwaiter().GetResult();
    //             }
    //             catch (Exception ex)
    //             {
    //                 // Console.WriteLine("Error: " + ex.Message);
    //             }
    //         }
    //     }

    //     Environment.Exit(0);
    // }

    static bool FpsLimit(int limit)
    {
        limit = 1000 / limit;
        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTimeL >= limit)
        {
            startTimeL = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            return true;
        }
        return false;
    }

    static int FpsCount()
    {
        frameCount++;
        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastCountTime >= 1000)
        {
            int result = frameCount;
            frameCount = 0;
            lastCountTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Console.WriteLine($"Running at {result} FPS. Ctrl + alt + t to turn of");
            return result;
        }
        else {
            return 0;
        }
    }
    static void GetScreenResolution(out int width, out int height)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"xdpyinfo | grep dimensions\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // output looks like: "  dimensions:    1920x1080 pixels (508x285 millimeters)"
        var match = Regex.Match(output, @"dimensions:\s+(\d+)x(\d+)");
        if (match.Success)
        {
            width = int.Parse(match.Groups[1].Value);
            height = int.Parse(match.Groups[2].Value);
        }
        else
        {
            width = 0;
            height = 0;
            throw new Exception("Could not parse screen resolution");
        }
    }
}
