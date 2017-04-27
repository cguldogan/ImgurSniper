﻿using ImgurSniper.Libraries.FFmpeg;
using ImgurSniper.Libraries.Helper;
using ImgurSniper.Libraries.ScreenCapture;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ImgurSniper {
    /// <summary>
    ///     Interaction logic for GifRecorder.xaml
    /// </summary>
    public partial class GifRecorder : IDisposable {
        private readonly Rectangle _size;
        private readonly bool _progressIndicatorEnabled;
        private ScreenRecorder _recorder;
        private string _outputMp4, _outputGif;
        private System.Timers.Timer _progressTimer;
        private bool _stopRequested;
        private double _startTime;

        public byte[] Gif;

        public GifRecorder(Rectangle size) {
            InitializeComponent();

            _size = size;

            Left = size.Left - 2;
            Top = size.Top - 2;
            Width = size.Width + 4;
            Height = size.Height + 4;

            Outline.Width = Width;
            Outline.Height = Height;

            int progressBarWidth = size.Width - 40;
            _progressIndicatorEnabled = progressBarWidth > 0;

            if (_progressIndicatorEnabled) {
                ProgressBar.Width = progressBarWidth;
                ProgressBar.Maximum = ConfigHelper.GifLength / 100d;

                //Space for ProgressBar
                Height += 30;
            } else {
                ProgressBar.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            BeginAnimation(OpacityProperty, Animations.FadeIn);
            new Thread(StartRecording).Start();
        }

        //OK Button click
        private void StopGifClick(object sender, MouseButtonEventArgs e) {
            if (_stopRequested)
                return;

            _stopRequested = true;

            StopRecording();
        }

        //Fade window out
        private void FadeOut(bool result) {
            Dispatcher.Invoke(() => {
                DoubleAnimation fadeOut = Animations.FadeOut;
                fadeOut.Completed += delegate {
                    try {
                        DialogResult = result;
                    } catch {
                        Close();
                    }
                };
                BeginAnimation(OpacityProperty, fadeOut);
            });
        }

        //Show circular Progressing Indicator
        private void ShowProgressBar() {
            DoubleAnimation fadeOut = Animations.FadeOut;
            fadeOut.Completed += delegate {
                OkButton.Visibility = Visibility.Collapsed;

                CircularProgressBar.Visibility = Visibility.Visible;
                CircularProgressBar.BeginAnimation(OpacityProperty, Animations.FadeIn);
            };
            OkButton.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void UpdateProgress(object sender, System.Timers.ElapsedEventArgs e) {
            try {
                Dispatcher.Invoke(() => {
                    if (_recorder == null || !_recorder.IsRecording) {
                        _progressTimer.Dispose();
                        return;
                    }

                    double currentTime = DateTime.Now.TimeOfDay.TotalMilliseconds;
                    double elapsed = (currentTime - _startTime);
                    ProgressBar.Value = elapsed / 100;

                    if (elapsed >= ConfigHelper.GifLength) {
                        StopRecording();
                        _progressTimer.Dispose();
                    }
                });
            } catch {
                // cannot use dispatcher on this window anymore
            }
        }

        #region GIF
        //Start Recording Video
        private void StartRecording() {
            try {
                //Clear left junk
                _outputMp4 = Path.Combine(Path.GetTempPath(), "screencapture.mp4");
                if (File.Exists(_outputMp4))
                    File.Delete(_outputMp4);
                _outputGif = Path.Combine(Path.GetTempPath(), "screencapture.gif");
                if (File.Exists(_outputGif))
                    File.Delete(_outputGif);

                //Path to FFmpeg.exe
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ffmpeg.exe");

                //FFmpeg.exe must be in ../ImgurSniper/Resources/ffmpeg.exe, install if not exists
                if (!File.Exists(ffmpegPath)) {
                    Process ffmpegHelper = new Process {
                        StartInfo = new ProcessStartInfo {
                            Arguments = "install",
                            FileName = Path.Combine(ConfigHelper.ProgramFiles, "FFmpegHelper.exe")
                        }
                    };
                    ffmpegHelper.Start();
                    ffmpegHelper.WaitForExit();

                    if (!File.Exists(ffmpegPath)) {
                        FadeOut(false);
                        return;
                    }
                }

                FFmpegOptions ffmpeg = new FFmpegOptions(ffmpegPath) {
                    VideoCodec = FFmpegVideoCodec.gif
                };

                //Parameters
                ScreencastOptions options = new ScreencastOptions {
                    CaptureArea = _size,
                    DrawCursor = ConfigHelper.ShowMouse,
                    Giffps = ConfigHelper.GifFps,
                    ScreenRecordFps = 30,
                    OutputPath = _outputMp4,
                    FFmpeg = ffmpeg,
                    //Dynamic Duration (Manual Stop)
                    Duration = 0
                };
                _recorder = new ScreenRecorder(options);

                //If Progressbar is enabled
                if (_progressIndicatorEnabled) {
                    _recorder.RecordingStarted += delegate {
                        //Update Progress
                        _progressTimer = new System.Timers.Timer {
                            Interval = 100
                        };
                        _progressTimer.Elapsed += UpdateProgress;
                        _progressTimer.Start();
                    };
                }

                _startTime = DateTime.Now.TimeOfDay.TotalMilliseconds;
                //Start Screen recording (block screen until StopRecording)
                _recorder.StartRecording();

                //Finish Gif (Convert)
                MakeGif();
            } catch {
                FadeOut(false);
            }

            if (!_stopRequested)
                Dispatcher.Invoke(StopRecording);
        }

        //Stop recording Video, begin encoding as GIF and Save
        private void StopRecording() {
            ShowProgressBar();
            DoneButton.Cursor = Cursors.Arrow;

            new Thread(_recorder.StopRecording).Start();
        }

        private void MakeGif() {
            //MP4 -> GIF
            bool success = _recorder.FFmpegEncodeAsGif(_outputGif);
            Gif = File.ReadAllBytes(_outputGif);

            _recorder.Dispose();
            _recorder = null;

            if (!success || !File.Exists(_outputGif) || (_recorder != null && _recorder.IsRecording))
                FadeOut(false);
            else
                FadeOut(true);
        }
        #endregion

        //IDisposable
        public void Dispose() {
            Gif = null;

            try {
                if (File.Exists(_outputGif)) {
                    File.Delete(_outputGif);
                }
                if (File.Exists(_outputMp4)) {
                    File.Delete(_outputMp4);
                }
            } catch {
                // could not delete
            }


            try {
                if (_recorder != null && _recorder.IsRecording)
                    _recorder.StopRecording();
            } catch {
                // unexpected error on stop recording
            }

            _recorder?.Dispose();
            _recorder = null;

            try {
                Close();
            } catch {
                //Window already closed
            }

            GC.Collect();
        }

    }
}