using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace QTECGolfEditor;

public partial class MainWindow : Window
{
    private string? _videoPath;
    private string? _musicPath;
    private TimeSpan _duration = TimeSpan.Zero;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _dragging;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            if (!_dragging && Player.Source != null && Player.NaturalDuration.HasTimeSpan)
            {
                Timeline.Value = Player.Position.TotalSeconds;
                UpdateTimeText();
            }
        };
        _timer.Start();
    }

    private string FfmpegPath
    {
        get
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
            if (File.Exists(bundled)) return bundled;
            return "ffmpeg";
        }
    }

    private void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "영상 파일|*.mp4;*.mov;*.avi;*.mkv;*.m4v|모든 파일|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        _videoPath = dialog.FileName;
        Player.Source = new Uri(_videoPath);
        Player.Play();
        Player.Pause();
        StatusText.Text = $"불러옴: {Path.GetFileName(_videoPath)}";
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!Player.NaturalDuration.HasTimeSpan) return;
        _duration = Player.NaturalDuration.TimeSpan;
        Timeline.Maximum = _duration.TotalSeconds;
        ImpactSlider.Maximum = _duration.TotalSeconds;
        ImpactSlider.Value = _duration.TotalSeconds * 0.65;
        UpdateTimeText();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (Player.Source == null) return;
        if (PlayButton.Content?.ToString()?.StartsWith("▶") == true)
        {
            Player.Play();
            PlayButton.Content = "⏸ 일시정지";
        }
        else
        {
            Player.Pause();
            PlayButton.Content = "▶ 재생";
        }
    }

    private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player.Source == null || Math.Abs(Player.Position.TotalSeconds - e.NewValue) < 0.35) return;
        _dragging = true;
        Player.Position = TimeSpan.FromSeconds(e.NewValue);
        UpdateTimeText();
        _dragging = false;
    }

    private void ImpactSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ImpactText == null) return;
        ImpactText.Text = $"타격 시점: {e.NewValue:0.00}초";
        if (Player.Source != null)
        {
            Player.Position = TimeSpan.FromSeconds(e.NewValue);
            Timeline.Value = e.NewValue;
        }
    }

    private void UpdateTimeText()
    {
        TimeText.Text = $"{Player.Position:mm\:ss} / {_duration:mm\:ss}";
    }

    private async void DetectImpact_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath == null)
        {
            MessageBox.Show("먼저 영상을 불러와 주세요.", "QTEC 골프 영상 편집기");
            return;
        }

        StatusText.Text = "임팩트 소리를 분석하고 있습니다...";
        Progress.IsIndeterminate = true;
        try
        {
            double second = await Task.Run(() => DetectAudioPeak(_videoPath));
            ImpactSlider.Value = Math.Clamp(second, 0, Math.Max(0, _duration.TotalSeconds));
            StatusText.Text = $"타격 순간을 {second:0.00}초로 찾았습니다. 필요하면 슬라이더로 조정하세요.";
        }
        catch
        {
            double fallback = _duration.TotalSeconds * 0.65;
            ImpactSlider.Value = fallback;
            StatusText.Text = "소리 분석이 어려워 예상 시점을 표시했습니다. 슬라이더로 조정하세요.";
        }
        finally
        {
            Progress.IsIndeterminate = false;
        }
    }

    private double DetectAudioPeak(string input)
    {
        string raw = Path.Combine(Path.GetTempPath(), $"qtec_audio_{Guid.NewGuid():N}.raw");
        try
        {
            RunFfmpeg(new[]
            {
                "-y", "-i", input, "-vn", "-ac", "1", "-ar", "8000", "-f", "s16le", raw
            });

            byte[] bytes = File.ReadAllBytes(raw);
            const int sampleRate = 8000;
            const int window = 400;
            double best = -1;
            int bestSample = 0;
            int totalSamples = bytes.Length / 2;
            int start = (int)(totalSamples * 0.10);
            int end = (int)(totalSamples * 0.92);

            for (int i = start; i + window < end; i += 80)
            {
                double energy = 0;
                for (int j = 0; j < window; j++)
                {
                    short value = BitConverter.ToInt16(bytes, (i + j) * 2);
                    energy += value * (double)value;
                }
                if (energy > best)
                {
                    best = energy;
                    bestSample = i;
                }
            }
            return bestSample / (double)sampleRate;
        }
        finally
        {
            if (File.Exists(raw)) File.Delete(raw);
        }
    }

    private void ChooseMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "음악 파일|*.mp3;*.wav;*.m4a;*.aac|모든 파일|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        _musicPath = dialog.FileName;
        MusicText.Text = Path.GetFileName(_musicPath);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath == null)
        {
            MessageBox.Show("먼저 영상을 불러와 주세요.", "QTEC 골프 영상 편집기");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "MP4 영상|*.mp4",
            FileName = Path.GetFileNameWithoutExtension(_videoPath) + "_편집.mp4"
        };
        if (dialog.ShowDialog() != true) return;

        ExportButton.IsEnabled = false;
        Progress.IsIndeterminate = true;
        StatusText.Text = "볼 궤적과 자막을 적용하고 있습니다...";

        try
        {
            var settings = new ExportSettings(ImpactSlider.Value, TrailColor.SelectedIndex, TrailCheck.IsChecked == true, SubtitleText.Text, _duration.TotalSeconds, _musicPath);
            await Task.Run(() => ExportVideo(dialog.FileName, settings));
            StatusText.Text = $"완료: {dialog.FileName}";
            MessageBox.Show("영상 저장이 완료되었습니다.", "QTEC 골프 영상 편집기");
        }
        catch (Exception ex)
        {
            StatusText.Text = "영상 저장 중 오류가 발생했습니다.";
            MessageBox.Show(ex.Message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportButton.IsEnabled = true;
            Progress.IsIndeterminate = false;
        }
    }

    private void ExportVideo(string output, ExportSettings settings)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "QTEC_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string assPath = Path.Combine(tempDir, "overlay.ass");
        try
        {
            File.WriteAllText(assPath, BuildAss(settings), new UTF8Encoding(true));
            string filterPath = assPath.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
            var args = new List<string> { "-y", "-i", _videoPath! };

            if (settings.MusicPath != null)
            {
                args.AddRange(new[] { "-stream_loop", "-1", "-i", settings.MusicPath });
            }

            args.AddRange(new[] { "-vf", $"ass='{filterPath}'" });

            if (settings.MusicPath != null)
            {
                args.AddRange(new[]
                {
                    "-filter_complex", "[0:a]volume=0.70[a0];[1:a]volume=0.22[a1];[a0][a1]amix=inputs=2:duration=first[a]",
                    "-map", "0:v", "-map", "[a]"
                });
            }
            else
            {
                args.AddRange(new[] { "-map", "0:v", "-map", "0:a?" });
            }

            args.AddRange(new[]
            {
                "-c:v", "libx264", "-profile:v", "high", "-pix_fmt", "yuv420p",
                "-crf", "20", "-preset", "medium", "-c:a", "aac", "-b:a", "192k",
                "-shortest", "-movflags", "+faststart", output
            });
            RunFfmpeg(args);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private string BuildAss()
    {
        double start = ImpactSlider.Value;
        double end = Math.Min(start + 4.2, _duration.TotalSeconds);
        string color = TrailColor.SelectedIndex switch
        {
            1 => "&H002828F5&",
            2 => "&H00FFFFFF&",
            3 => "&H00FF8214&",
            _ => "&H0000EBFF&"
        };

        string dots = "m 964 698 b 964 691 970 685 977 685 b 984 685 990 691 990 698 b 990 705 984 711 977 711 b 970 711 964 705 964 698 " +
                      "m 963 643 b 963 637 968 632 974 632 b 980 632 985 637 985 643 b 985 649 980 654 974 654 b 968 654 963 649 963 643 " +
                      "m 962 585 b 962 579 967 574 973 574 b 979 574 984 579 984 585 b 984 591 979 596 973 596 b 967 596 962 591 962 585 " +
                      "m 964 525 b 964 519 969 514 975 514 b 981 514 986 519 986 525 b 986 531 981 536 975 536 b 969 536 964 531 964 525 " +
                      "m 968 466 b 968 460 973 455 979 455 b 985 455 990 460 990 466 b 990 472 985 477 979 477 b 973 477 968 472 968 466 " +
                      "m 975 411 b 975 405 980 400 986 400 b 992 400 997 405 997 411 b 997 417 992 422 986 422 b 980 422 975 417 975 411 " +
                      "m 984 361 b 984 355 989 350 995 350 b 1001 350 1006 355 1006 361 b 1006 367 1001 372 995 372 b 989 372 984 367 984 361 " +
                      "m 995 316 b 995 310 1000 305 1006 305 b 1012 305 1017 310 1017 316 b 1017 322 1012 327 1006 327 b 1000 327 995 322 995 316 " +
                      "m 1007 279 b 1007 273 1012 268 1018 268 b 1024 268 1029 273 1029 279 b 1029 285 1024 290 1018 290 b 1012 290 1007 285 1007 279";

        string safeSubtitle = settings.Subtitle.Replace("\\", "／").Replace("{", "（").Replace("}", "）").Replace("\n", "\\N");
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("PlayResX: 1920");
        sb.AppendLine("PlayResY: 1080");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine("Style: Default,Malgun Gothic,52,&H0000EBFF,&H0000EBFF,&H00101010,&H80000000,-1,0,0,0,100,100,0,0,1,4,1,8,20,20,65,1");
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        if (settings.ShowTrail)
        {
            sb.AppendLine($"Dialogue: 0,{AssTime(start)},{AssTime(end)},Default,,0,0,0,,{{\\an7\\pos(0,0)\\p1\\bord4\\shad0\\c{color}\\fad(150,650)}}{dots}");
        }
        if (!string.IsNullOrWhiteSpace(safeSubtitle))
        {
            sb.AppendLine($"Dialogue: 1,{AssTime(start)},{AssTime(end)},Default,,0,0,0,,{{\\fad(180,650)}}{safeSubtitle}");
        }
        return sb.ToString();
    }

    private static string AssTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    private void RunFfmpeg(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("영상 처리 프로그램을 시작할 수 없습니다.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("영상 처리에 실패했습니다.\n" + error.Split('\n').TakeLast(8).Aggregate("", (a, b) => a + b + "\n"));
    }
}
