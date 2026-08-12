using System.IO;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

Console.WriteLine("Portal 2 TTS Helper");
Console.WriteLine("-------------------");


// ============================================================
// CONFIGURATION
// ============================================================

string configFile = Path.Combine(
    AppContext.BaseDirectory,
    "config.txt"
);

string voice1Name = "Microsoft David";
string voice2Name = "Microsoft Zira";

int speechRate = 0;
int speechVolume = 100;

if (File.Exists(configFile))
{
  foreach (string rawLine in File.ReadAllLines(configFile))
  {
    string line = rawLine.Trim();

    if (string.IsNullOrWhiteSpace(line))
      continue;

    if (line.StartsWith("#"))
      continue;

    int equalsIndex = line.IndexOf('=');

    if (equalsIndex <= 0)
      continue;

    string key = line[..equalsIndex].Trim();
    string value = line[(equalsIndex + 1)..].Trim();

    switch (key.ToLower())
    {
      case "voice1":
        voice1Name = value;
        break;

      case "voice2":
        voice2Name = value;
        break;

      case "rate":
        if (int.TryParse(value, out int parsedRate))
        {
          speechRate = Math.Clamp(parsedRate, -10, 10);
        }
        break;

      case "volume":
        if (int.TryParse(value, out int parsedVolume))
        {
          speechVolume = Math.Clamp(parsedVolume, 0, 100);
        }
        break;
    }
  }
}
else
{
  Console.WriteLine("WARNING: config.txt was not found.");
  Console.WriteLine("Using default voices.");
  Console.WriteLine();
}


// ============================================================
// FIND PORTAL 2
// ============================================================

string? portal2Path = FindPortal2();

if (portal2Path == null)
{
  Console.WriteLine("ERROR: Could not find Portal 2.");
  Console.WriteLine();
  Console.WriteLine("Make sure Portal 2 is installed through Steam.");
  Console.WriteLine();
  Console.WriteLine("Press Enter to exit...");
  Console.ReadLine();
  return;
}

string logFile = Path.Combine(
    portal2Path,
    "portal2",
    "console.log"
);

Console.WriteLine($"Portal 2 found:");
Console.WriteLine(portal2Path);
Console.WriteLine();


// ============================================================
// CHECK CONSOLE LOG
// ============================================================

if (!File.Exists(logFile))
{
  Console.WriteLine("ERROR: Portal 2 console.log was not found.");
  Console.WriteLine();
  Console.WriteLine("Make sure Portal 2 has been started with:");
  Console.WriteLine("-condebug");
  Console.WriteLine();
  Console.WriteLine($"Expected:");
  Console.WriteLine(logFile);

  Console.ReadLine();
  return;
}


// ============================================================
// CREATE SPEECH SYNTHESIZERS
// ============================================================

using SpeechSynthesizer synth1 = new SpeechSynthesizer();
using SpeechSynthesizer synth2 = new SpeechSynthesizer();


// Find the requested voices.

bool voice1Found = TrySelectVoice(
    synth1,
    voice1Name
);

bool voice2Found = TrySelectVoice(
    synth2,
    voice2Name
);

if (!voice1Found)
{
  Console.WriteLine(
      $"WARNING: Voice '{voice1Name}' was not found."
  );

  Console.WriteLine(
      $"Using Windows default voice for Player 1."
  );
}

if (!voice2Found)
{
  Console.WriteLine(
      $"WARNING: Voice '{voice2Name}' was not found."
  );

  Console.WriteLine(
      $"Using Windows default voice for Player 2."
  );
}

synth1.Rate = speechRate;
synth1.Volume = speechVolume;

synth2.Rate = speechRate;
synth2.Volume = speechVolume;


// ============================================================
// SHOW CONFIGURATION
// ============================================================

Console.WriteLine();
Console.WriteLine("Voice configuration:");
Console.WriteLine($"Voice 1: {synth1.Voice.Name}");
Console.WriteLine($"Voice 2: {synth2.Voice.Name}");
Console.WriteLine($"Rate:    {speechRate}");
Console.WriteLine($"Volume:  {speechVolume}");
Console.WriteLine();


// ============================================================
// OPEN PORTAL 2 CONSOLE LOG
// ============================================================

Console.WriteLine("Watching Portal 2 console.log...");
Console.WriteLine("Listening for all player chat.");
Console.WriteLine();

using FileStream fileStream = new FileStream(
    logFile,
    FileMode.Open,
    FileAccess.Read,
    FileShare.ReadWrite
);

using StreamReader reader = new StreamReader(fileStream);


// Start at the end of the existing log.
//
// This prevents old chat messages from being spoken.
fileStream.Seek(0, SeekOrigin.End);


// ============================================================
// PLAYER → VOICE ASSIGNMENTS
// ============================================================

Dictionary<string, int> playerVoices =
    new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase
    );


// ============================================================
// DUPLICATE FILTER
// ============================================================

const int DuplicateWindowMilliseconds = 250;

string? lastMessage = null;
DateTime lastMessageTime = DateTime.MinValue;


// ============================================================
// MAIN LOOP
// ============================================================

while (true)
{
  string? line = await reader.ReadLineAsync();

  if (line == null)
  {
    await Task.Delay(50);
    continue;
  }


  // --------------------------------------------------------
  // Find:
  //
  // PlayerName: Message
  // --------------------------------------------------------

  int colonIndex = line.IndexOf(':');

  if (colonIndex <= 0)
    continue;


  string playerName =
      line[..colonIndex].Trim();

  string message =
      line[(colonIndex + 1)..].Trim();


  if (string.IsNullOrWhiteSpace(playerName) ||
      string.IsNullOrWhiteSpace(message))
  {
    continue;
  }


  // --------------------------------------------------------
  // Assign a voice to new players.
  // --------------------------------------------------------

  if (!playerVoices.ContainsKey(playerName))
  {
    if (!playerVoices.ContainsValue(1))
    {
      playerVoices[playerName] = 1;

      Console.WriteLine(
          $"Assigned {playerName} → Voice 1 ({synth1.Voice.Name})"
      );
    }
    else if (!playerVoices.ContainsValue(2))
    {
      playerVoices[playerName] = 2;

      Console.WriteLine(
          $"Assigned {playerName} → Voice 2 ({synth2.Voice.Name})"
      );
    }
    else
    {
      // More than two names have appeared.
      // Ignore additional names.
      Console.WriteLine(
          $"Ignoring additional player: {playerName}"
      );

      continue;
    }
  }


  // --------------------------------------------------------
  // Duplicate protection.
  // --------------------------------------------------------

  DateTime now = DateTime.UtcNow;

  string currentChat =
      playerName + ":" + message;

  bool isDuplicate =
      string.Equals(
          currentChat,
          lastMessage,
          StringComparison.Ordinal
      )
      &&
      (now - lastMessageTime).TotalMilliseconds
          <= DuplicateWindowMilliseconds;

  if (isDuplicate)
  {
    Console.WriteLine(
        $"IGNORED DUPLICATE: {playerName}: {message}"
    );

    continue;
  }

  lastMessage = currentChat;
  lastMessageTime = now;


  // --------------------------------------------------------
  // Display chat.
  // --------------------------------------------------------

  int assignedVoice =
      playerVoices[playerName];

  Console.WriteLine(
      $"CHAT [{synthVoiceName(assignedVoice)}] " +
      $"{playerName}: {message}"
  );


  // --------------------------------------------------------
  // Speak.
  // --------------------------------------------------------

  try
  {
    if (assignedVoice == 1)
    {
      synth1.SpeakAsyncCancelAll();
      synth1.SpeakAsync(message);
    }
    else
    {
      synth2.SpeakAsyncCancelAll();
      synth2.SpeakAsync(message);
    }
  }
  catch (Exception ex)
  {
    Console.WriteLine(
        $"TTS error: {ex.Message}"
    );
  }
}


// ============================================================
// HELPER FUNCTIONS
// ============================================================

static string synthVoiceName(int voiceNumber)
{
  return voiceNumber == 1
      ? "Voice 1"
      : "Voice 2";
}


static bool TrySelectVoice(
    SpeechSynthesizer synthesizer,
    string requestedVoice)
{
  foreach (InstalledVoice voice
      in synthesizer.GetInstalledVoices())
  {
    if (string.Equals(
        voice.VoiceInfo.Name,
        requestedVoice,
        StringComparison.OrdinalIgnoreCase))
    {
      synthesizer.SelectVoice(
          voice.VoiceInfo.Name
      );

      return true;
    }
  }

  return false;
}


// ============================================================
// STEAM / PORTAL 2 DETECTION
// ============================================================

static string? FindPortal2()
{
  List<string> steamRoots = new List<string>();


  // Common Steam locations.

  string[] possibleSteamLocations =
  {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam"
    };


  foreach (string location
      in possibleSteamLocations)
  {
    if (Directory.Exists(location))
    {
      steamRoots.Add(location);
    }
  }


  // Check Windows registry.

  try
  {
    using Microsoft.Win32.RegistryKey? key =
        Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\WOW6432Node\Valve\Steam"
        );

    string? steamPath =
        key?.GetValue("InstallPath") as string;

    if (!string.IsNullOrWhiteSpace(steamPath) &&
        Directory.Exists(steamPath))
    {
      if (!steamRoots.Contains(steamPath))
      {
        steamRoots.Add(steamPath);
      }
    }
  }
  catch
  {
    // Registry lookup isn't required.
  }


  foreach (string steamRoot
      in steamRoots)
  {
    string libraryFile =
        Path.Combine(
            steamRoot,
            "steamapps",
            "libraryfolders.vdf"
        );

    if (!File.Exists(libraryFile))
      continue;


    string vdf =
        File.ReadAllText(libraryFile);


    MatchCollection matches =
        Regex.Matches(
            vdf,
            @"""path""\s+""([^""]+)""",
            RegexOptions.IgnoreCase
        );


    List<string> libraries =
        new List<string>();


    libraries.Add(steamRoot);


    foreach (Match match in matches)
    {
      string library =
          match.Groups[1].Value
              .Replace(@"\\", @"\");


      if (Directory.Exists(library) &&
          !libraries.Contains(library))
      {
        libraries.Add(library);
      }
    }


    foreach (string library
        in libraries)
    {
      string portal2 =
          Path.Combine(
              library,
              "steamapps",
              "common",
              "Portal 2"
          );


      if (File.Exists(
          Path.Combine(
              portal2,
              "portal2.exe"
          )))
      {
        return portal2;
      }
    }
  }


  return null;
}
