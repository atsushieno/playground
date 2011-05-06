﻿using System;
using System.Runtime.InteropServices;

using Android.App;
using Android.Content;
using Android.Media;
using Android.Preferences;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System.IO;
using System.Threading;
using UnmanagedOgg;

using Stream = System.IO.Stream;

namespace Falplayer
{
    [Activity(Label = "Falplayer", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnPause()
        {
            player.Pause ();
            base.OnPause();
        }
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            player = new Player (this);
        }
        Player player;
    }

    class PlayerView : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        Player player;
        Activity activity;
        Button load_button, play_button, stop_button;
        TextView title_text_view;
        SeekBar seekbar;
        long loop_start, loop_length, loop_end;
        int loops;

        public PlayerView (Player player, Activity activity)
        {
            this.player = player;
            this.activity = activity;
            this.load_button = activity.FindViewById<Button>(Resource.Id.SelectButton);
            this.play_button = activity.FindViewById<Button>(Resource.Id.PlayButton);
            this.stop_button = activity.FindViewById<Button>(Resource.Id.StopButton);
            this.seekbar = activity.FindViewById<SeekBar>(Resource.Id.SongSeekbar);
            this.title_text_view = activity.FindViewById<TextView>(Resource.Id.SongTitleTextView);

            load_button.Click += delegate {
                var db = new AlertDialog.Builder (activity);
                db.SetTitle ("Select Music Folder");
                var pref = PreferenceManager.GetDefaultSharedPreferences (activity);
#if false
                var dirs = pref.GetString ("file_group", "ZAPZAPZAP!!!").Split('\\');
#else
                var dirs = new string[] { "/sdcard/falcom/ED_SORA3", "/sdcard/falcom/YSO" };
                var edit = pref.Edit();
                edit.PutString ("file_group", String.Join("\\", dirs));
                edit.Commit ();
#endif
                db.SetItems (dirs, delegate (object o, DialogClickEventArgs e) {
                    // FIXME: MfA issue; e.Which or something equivalent should be int, not limited enum.
                    Android.Util.Log.Debug ("FALPLAYER", "selected item: " + (int) e.Which);
                });
                var dlg = db.Show ();
                player.SelectFile();
            };

            play_button.Click += delegate {
                try {
                    if (player.IsPlaying) {
                        player.Pause ();
                        play_button.Text = "Play";
                    } else {
                        player.Play ();
                        play_button.Text = "Pause";
                    }
                } catch (Exception ex) {
                    play_button.Text = ex.Message;
                }
            };
        }

        public void Initialize (long totalLength, long loopStart, long loopLength, long loopEnd)
        {
            loops = 0;
            loop_start = loopStart;
            loop_length = loopLength;
            loop_end = loopEnd;
            PlayerEnabled = true;

            play_button.Text = "Play";
            title_text_view.Text = string.Format ("loop: {0} - {1} - {2}", loopStart, loopLength, totalLength);
            // Since our AudioTrack bitrate is fake, those markers must be faked too.
            seekbar.Max = (int) totalLength;
            seekbar.SecondaryProgress = (int) loopEnd;
            seekbar.SetOnSeekBarChangeListener (this);
        }

        public bool PlayerEnabled {
            get { return play_button.Enabled; }
            set {
                activity.RunOnUiThread (delegate {
                    play_button.Enabled = value;
                    seekbar.Enabled = value;
                    });
            }
        }

        public void Error (string msgbase, params object[] args)
        {
            activity.RunOnUiThread (delegate {
                PlayerEnabled = false;
                play_button.Text = String.Format(msgbase, args);
                });
        }

        public void ReportProgress (long pos)
        {
            activity.RunOnUiThread (delegate {
                title_text_view.Text = String.Format("loop: {0} / cur {1} / end {2}", loops, pos, loop_end);
                seekbar.Progress = (int) pos;
            });
        }

        public void ProcessLoop (long resetPosition)
        {
            loops++;
            seekbar.Progress = (int)resetPosition;
        }

        public void ProcessComplete ()
        {
        }

        public void OnProgressChanged (SeekBar seekBar, int progress, bool fromUser)
        {
            if (!fromUser)
                return;
            player.Seek (progress);
        }

        public void OnStartTrackingTouch (SeekBar seekBar)
        {
            // do nothing
        }

        public void OnStopTrackingTouch (SeekBar seekBar)
        {
            // do nothing
        }
    }

    class Player
    {
        PlayerView view;
        AudioTrack audio;
        OggStreamBuffer vorbis_buffer;
        LoopCommentExtension loop;
        PlayerAsyncTask task;

        static readonly int min_buf_size = AudioTrack.GetMinBufferSize(22050, (int)ChannelConfiguration.Stereo, Encoding.Pcm16bit);
        int buf_size = min_buf_size * 8;

        public Player (Activity activity)
        {
            view = new PlayerView (this, activity);
            audio = new AudioTrack (Android.Media.Stream.Music, 22050, ChannelConfiguration.Stereo, Android.Media.Encoding.Pcm16bit, buf_size * 5, AudioTrackMode.Stream);
            task = new PlayerAsyncTask (this);

            SelectFile ();
        }

        public void SelectFile ()
        {
            Stream input = File.OpenRead ("/sdcard/ED6437.ogg");
            vorbis_buffer = new UnmanagedOgg.OggStreamBuffer(input);
            loop = new LoopCommentExtension (vorbis_buffer);
            InitializeVorbisBuffer ();
        }

        public void InitializeVorbisBuffer ()
        {
            view.Initialize (loop.Total * 4, loop.Start * 4, loop.Length * 4, loop.End * 4);
            task.LoadVorbisBuffer(vorbis_buffer, loop);
        }

        public LoopCommentExtension Loop {
            get { return loop; }
        }

        public bool IsPlaying
        {
            get { return audio.PlayState == PlayState.Playing; }
        }

        public void Play ()
        {
            if (audio.PlayState == PlayState.Paused)
                task.Resume ();
            else {
                task = new PlayerAsyncTask (this);
                InitializeVorbisBuffer ();
                task.Execute ();
            }
        }

        public void Pause ()
        {
            task.Pause ();
        }

        public void Stop ()
        {
            task.Cancel (true);
        }

        public void Seek (long pos)
        {
            task.Seek (pos);
        }

        class PlayerAsyncTask : AsyncTask
        {
            Player player;
            bool pause, stop;
            ManualResetEvent pause_handle = new ManualResetEvent (false);
            int x;
            byte [] buffer;
            long total;

            public PlayerAsyncTask (Player player)
            {
                this.player = player;
            }

            public void LoadVorbisBuffer (OggStreamBuffer ovb, LoopCommentExtension loop)
            {
                total = loop.Total;
            }

            public void Pause ()
            {
                pause = true;
            }

            public void Resume ()
            {
                pause = false; // make sure to not get overwritten
                pause_handle.Set ();
            }

            public void Seek (long pos)
            {
                total = pos;
                player.vorbis_buffer.SeekPcm (pos / 4);
            }

            protected override void OnCancelled ()
            {
                pause_handle.Set ();
                if (player.IsPlaying)
                    stop = true; // and let player loop finish.
                base.OnCancelled ();
            }

            protected override Java.Lang.Object DoInBackground(params Java.Lang.Object[] @params)
            {
                return DoRun();
            }

            Java.Lang.Object DoRun()
            {
                x = 0;
                total = 0;
                long loop_start = player.Loop.Start * 4, loop_length = player.Loop.Length * 4, loop_end = player.Loop.End * 4;
                buffer = new byte [player.buf_size / 4];

                player.audio.Play ();
                while (!stop)
                {
                    if (pause) {
                        pause = false;
                        pause_handle.WaitOne ();
                    }
                    long ret = player.vorbis_buffer.Read (buffer, 0, buffer.Length);
                    if (ret <= 0 || ret > buffer.Length)
                    {
                        stop = true;
                        if (ret < 0)
                            player.view.Error ("vorbis error : {0}", ret);
                        else if (ret > buffer.Length)
                            player.view.Error ("buffering overflow : {0}", ret);
                        else
                            player.view.ProcessComplete ();
                        break;
                    }

                    if (ret + total >= loop_end)
                        ret = loop_end - total; // cut down the buffer after loop

                    if (++x % 50 == 0)
                        player.view.ReportProgress (total);

                    // downgrade bitrate
                    for (int i = 1; i < ret / 2; i++)
                        buffer[i] = buffer[i * 2 + 1];
                    player.audio.Write (buffer, 0, (int) ret / 2);
                    player.audio.Flush ();
                    total += ret;
                    // loop back to LOOPSTART
                    if (total >= loop_end)
                    {
                        player.view.ProcessLoop(loop_start);
                        player.vorbis_buffer.SeekPcm (loop_start / 4); // also faked
                        total = loop_start;
                    }
                }
                player.audio.Stop ();
                player.view.ProcessComplete ();
                return null;
            }
        }
    }

    public class LoopCommentExtension
    {
        long loop_start = 0, loop_length = int.MaxValue, loop_end = int.MaxValue, total;

        public LoopCommentExtension (OggStreamBuffer owner)
        {
            total = owner.GetTotalPcm (-1);
            foreach (var cmt in owner.GetComment(-1).Comments)
            {
                var comment = cmt.Replace(" ", ""); // trim spaces
                if (comment.StartsWith("LOOPSTART="))
                    loop_start = int.Parse(comment.Substring("LOOPSTART=".Length));
                if (comment.StartsWith("LOOPLENGTH="))
                    loop_length = int.Parse(comment.Substring("LOOPLENGTH=".Length));
            }

            if (loop_start > 0 && loop_length > 0)
                loop_end = (loop_start + loop_length);
        }

        public long Start {
            get { return loop_start; }
        }

        public long Length {
            get { return loop_length; }
        }

        public long End {
            get { return loop_end; }
        }

        public long Total
        {
            get { return total; }
        }
    }
}
