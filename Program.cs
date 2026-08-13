using System.Media;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using SherpaOnnx;

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
string voice3Name = "GLaDOS";

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

    switch (key.ToLowerInvariant())
    {
      case "voice1":
        voice1Name = value;
        break;

      case "voice2":
        voice2Name = value;
        break;

      case "voice3":
        voice3Name = value;
        break;

      case "rate":
        if (int.TryParse(value, out int parsedRate))
        {
          speechRate = Math.Clamp(
              parsedRate,
              -10,
              10
          );
        }
        break;

      case "volume":
        if (int.TryParse(value, out int parsedVolume))
        {
          speechVolume = Math.Clamp(
              parsedVolume,
              0,
              100
          );
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

Console.WriteLine("Portal 2 found:");
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
  Console.WriteLine("Expected:");
  Console.WriteLine(logFile);
  Console.ReadLine();
  return;
}


// ============================================================
// CREATE WINDOWS SPEECH SYNTHESIZERS
// ============================================================

using SpeechSynthesizer synth1 = new SpeechSynthesizer();
using SpeechSynthesizer synth2 = new SpeechSynthesizer();

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
      "Using Windows default voice for Voice 1."
  );
}

if (!voice2Found)
{
  Console.WriteLine(
      $"WARNING: Voice '{voice2Name}' was not found."
  );

  Console.WriteLine(
      "Using Windows default voice for Voice 2."
  );
}

synth1.Rate = speechRate;
synth1.Volume = speechVolume;

synth2.Rate = speechRate;
synth2.Volume = speechVolume;


// ============================================================
// FIND GLaDOS MODEL
// ============================================================

string gladosDirectory = FindGladosDirectory();

string gladosModel = Path.Combine(
    gladosDirectory,
    "en_US-glados.onnx"
);

string gladosTokens = Path.Combine(
    gladosDirectory,
    "tokens.txt"
);

string gladosDataDir = Path.Combine(
    gladosDirectory,
    "espeak-ng-data"
);

Console.WriteLine("GLaDOS model directory:");
Console.WriteLine(gladosDirectory);
Console.WriteLine();


// ============================================================
// CHECK GLaDOS MODEL
// ============================================================

bool gladosAvailable =
    File.Exists(gladosModel) &&
    File.Exists(gladosTokens) &&
    Directory.Exists(gladosDataDir);

if (!gladosAvailable)
{
  Console.WriteLine(
      "WARNING: GLaDOS model files were not found."
  );

  Console.WriteLine(
      $"Model:  {gladosModel}"
  );

  Console.WriteLine(
      $"Tokens: {gladosTokens}"
  );

  Console.WriteLine(
      $"Data:   {gladosDataDir}"
  );

  Console.WriteLine(
      "GLaDOS will be unavailable."
  );

  Console.WriteLine();
}


// ============================================================
// INITIALIZE GLaDOS TTS
// ============================================================

OfflineTts? gladosTts = null;

if (gladosAvailable)
{
  try
  {
    Console.WriteLine(
        "Loading GLaDOS TTS model..."
    );

    OfflineTtsConfig gladosConfig =
        new OfflineTtsConfig();

    gladosConfig.Model.Vits.Model =
        gladosModel;

    gladosConfig.Model.Vits.Tokens =
        gladosTokens;

    gladosConfig.Model.Vits.DataDir =
        gladosDataDir;

    gladosConfig.Model.NumThreads = 2;
    gladosConfig.Model.Provider = "cpu";
    gladosConfig.Model.Debug = 0;
    gladosConfig.MaxNumSentences = 1;

    gladosTts =
        new OfflineTts(
            gladosConfig
        );

    Console.WriteLine(
        "GLaDOS TTS loaded successfully."
    );
  }
  catch (Exception ex)
  {
    Console.WriteLine(
        $"ERROR loading GLaDOS TTS: {ex.Message}"
    );

    Console.WriteLine(
        "GLaDOS will be unavailable."
    );
  }
}

Console.WriteLine();


// ============================================================
// SHOW CONFIGURATION
// ============================================================

Console.WriteLine("Voice configuration:");
Console.WriteLine($"Voice 1: {synth1.Voice.Name}");
Console.WriteLine($"Voice 2: {synth2.Voice.Name}");
Console.WriteLine($"Voice 3: {voice3Name}");
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

using StreamReader reader =
    new StreamReader(fileStream);


// Start at the end of the existing log.
// This prevents old messages from being spoken.

fileStream.Seek(
    0,
    SeekOrigin.End
);


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

string? lastSeenChat = null;

// ============================================================
// MAIN LOOP
// ============================================================

while (true)
{
  string? line =
      await reader.ReadLineAsync();

  if (line == null)
  {
    await Task.Delay(50);
    continue;
  }


  // --------------------------------------------------------
  // Find the colon used by Portal 2 chat:
  //
  // PlayerName: Message
  // --------------------------------------------------------

  int colonIndex =
      line.IndexOf(':');

  if (colonIndex <= 0)
    continue;


  string playerName =
      line[..colonIndex].Trim();

  string message =
      line[(colonIndex + 1)..].Trim();


  if (
      string.IsNullOrWhiteSpace(playerName) ||
      string.IsNullOrWhiteSpace(message)
  )
  {
    continue;
  }


  // --------------------------------------------------------
  // Make sure this actually looks like player chat.
  // --------------------------------------------------------

  if (playerName.Length > 32)
    continue;

  if (playerName.Contains(' '))
    continue;


  // --------------------------------------------------------
  // Assign a voice to new players.
  //
  // First player -> GLaDOS
  // Second player -> Voice 1
  // Third player -> Voice 2
  // --------------------------------------------------------

  if (!playerVoices.ContainsKey(playerName))
  {
    if (
        gladosTts != null &&
        !playerVoices.ContainsValue(3)
    )
    {
      playerVoices[playerName] = 3;

      Console.WriteLine(
          $"Assigned {playerName} → GLaDOS"
      );
    }
    else if (!playerVoices.ContainsValue(1))
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
      Console.WriteLine(
          $"Ignoring additional player: {playerName}"
      );

      continue;
    }
  }


// --------------------------------------------------------
// Duplicate protection.
//
// Portal 2 writes duplicate chat lines consecutively.
// We therefore compare against the immediately previous
// chat line instead of using a time window.
//
// This also works when GLaDOS is busy speaking a long
// message and the duplicate is waiting in console.log.
// --------------------------------------------------------

string currentChat =
    playerName + ":" + message;

if (
    string.Equals(
        currentChat,
        lastSeenChat,
        StringComparison.Ordinal
    )
)
{
  Console.WriteLine(
      $"IGNORED DUPLICATE: {playerName}: {message}"
  );

  continue;
}

lastSeenChat = currentChat;


// --------------------------------------------------------
// Display chat.
// --------------------------------------------------------

int assignedVoice =
      playerVoices[playerName];

  Console.WriteLine(
      $"CHAT [{VoiceLabel(assignedVoice)}] " +
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
    else if (assignedVoice == 2)
    {
      synth2.SpeakAsyncCancelAll();

      synth2.SpeakAsync(message);
    }
    else if (
        assignedVoice == 3 &&
        gladosTts != null
    )
    {
      await SpeakGlados(
          gladosTts,
          message
      );
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
// GLaDOS SPEECH
// ============================================================

static async Task SpeakGlados(
    OfflineTts tts,
    string text
)
{
  string temporaryWave =
      Path.Combine(
          Path.GetTempPath(),
          $"portal2_glados_{Guid.NewGuid():N}.wav"
      );

  try
  {
    OfflineTtsGenerationConfig generationConfig =
        new OfflineTtsGenerationConfig();

    generationConfig.Sid = 0;
    generationConfig.Speed = 1.0f;
    generationConfig.SilenceScale = 0.2f;


    var audio =
        await Task.Run(
            () =>
                tts.GenerateWithConfig(
                    text,
                    generationConfig,
                    null
                )
        );


    bool saved =
        audio.SaveToWaveFile(
            temporaryWave
        );

    audio.Dispose();


    if (!saved)
    {
      throw new Exception(
          "GLaDOS failed to save the generated WAV."
      );
    }


    using SoundPlayer player =
        new SoundPlayer(
            temporaryWave
        );

    player.Load();
    player.PlaySync();
  }
  finally
  {
    try
    {
      if (File.Exists(temporaryWave))
        File.Delete(temporaryWave);
    }
    catch
    {
    }
  }
}


// ============================================================
// VOICE LABEL
// ============================================================

static string VoiceLabel(
    int voiceNumber
)
{
  return voiceNumber switch
  {
    1 => "Voice 1",
    2 => "Voice 2",
    3 => "GLaDOS",
    _ => "Unknown"
  };
}


// ============================================================
// SELECT WINDOWS VOICE
// ============================================================

static bool TrySelectVoice(
    SpeechSynthesizer synthesizer,
    string requestedVoice
)
{
  foreach (
      InstalledVoice voice
      in synthesizer.GetInstalledVoices()
  )
  {
    if (
        string.Equals(
            voice.VoiceInfo.Name,
            requestedVoice,
            StringComparison.OrdinalIgnoreCase
        )
    )
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
// FIND GLaDOS DIRECTORY
// ============================================================

static string FindGladosDirectory()
{
  string executableDirectory =
      AppContext.BaseDirectory;


  string projectRelative =
      Path.GetFullPath(
          Path.Combine(
              executableDirectory,
              "..",
              "..",
              "..",
              "glados",
              "vits-piper-en_US-glados"
          )
      );


  if (
      File.Exists(
          Path.Combine(
              projectRelative,
              "en_US-glados.onnx"
          )
      )
  )
  {
    return projectRelative;
  }


  const string knownPath =
      @"C:\Mods\DllMods\Portal2TTS\glados\vits-piper-en_US-glados";


  return knownPath;
}


// ============================================================
// STEAM / PORTAL 2 DETECTION
// ============================================================

static string? FindPortal2()
{
  List<string> steamRoots =
      new List<string>();


  string[] possibleSteamLocations =
  {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam"
    };


  foreach (
      string location
      in possibleSteamLocations
  )
  {
    if (Directory.Exists(location))
    {
      steamRoots.Add(location);
    }
  }


  try
  {
    using Microsoft.Win32.RegistryKey? key =
        Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\WOW6432Node\Valve\Steam"
        );

    string? steamPath =
        key?.GetValue(
            "InstallPath"
        ) as string;


    if (
        !string.IsNullOrWhiteSpace(steamPath) &&
        Directory.Exists(steamPath)
    )
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


  foreach (
      string steamRoot
      in steamRoots
  )
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
        File.ReadAllText(
            libraryFile
        );


    MatchCollection matches =
        Regex.Matches(
            vdf,
            @"""path""\s+""([^""]+)""",
            RegexOptions.IgnoreCase
        );


    List<string> libraries =
        new List<string>();


    libraries.Add(
        steamRoot
    );


    foreach (
        Match match
        in matches
    )
    {
      string library =
          match.Groups[1].Value
              .Replace(
                  @"\\",
                  @"\"
              );


      if (
          Directory.Exists(library) &&
          !libraries.Contains(library)
      )
      {
        libraries.Add(library);
      }
    }


    foreach (
        string library
        in libraries
    )
    {
      string portal2 =
          Path.Combine(
              library,
              "steamapps",
              "common",
              "Portal 2"
          );


      if (
          File.Exists(
              Path.Combine(
                  portal2,
                  "portal2.exe"
              )
          )
      )
      {
        return portal2;
      }
    }
  }


  return null;
}