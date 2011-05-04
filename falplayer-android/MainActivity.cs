using System;
using System.Runtime.InteropServices;

using Android.App;
using Android.Content;
using Android.Media;
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
            player_task.Cancel(true);
            base.OnPause();
        }
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            //Button load_button = FindViewById<Button> (Resource.Id.SelectButton);
            Button play_button = FindViewById<Button> (Resource.Id.PlayButton);

            player_task = new PlayerAsyncTask (this);

            //load_button.Click += delegate {
                Stream input = File.OpenRead ("/sdcard/ED6437.ogg");
                var vorbis_buffer = new UnmanagedOgg.OggStreamBuffer (input);
                player_task.LoadVorbisBuffer (vorbis_buffer);
            //};
        }
        PlayerAsyncTask player_task;
    }

    class PlayerView : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        PlayerAsyncTask player;
        Activity activity;
        Button load_button, play_button;
        SeekBar seekbar;
        long loop_start, loop_length, loop_end;
        int loops;
        bool is_playing;

        public PlayerView (PlayerAsyncTask player, Activity activity)
        {
            this.player = player;
            this.activity = activity;
            //this.load_button = activity.FindViewById<Button>(Resource.Id.SelectButton);
            this.play_button = activity.FindViewById<Button>(Resource.Id.PlayButton);
            this.seekbar = activity.FindViewById<SeekBar> (Resource.Id.SongSeekbar);

            play_button.Click += delegate {
                try {
                    if (is_playing) {
                        player.Cancel (true);
                    }
                    else
                        player.Execute ();
                    is_playing = !is_playing;
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

            play_button.Text = string.Format("loop: {0} - {1} - {2}", loopStart, loopLength, totalLength);
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
                activity.RunOnUiThread(delegate { play_button.Text = String.Format("looped: {0} / cur {1} / end {2}", loops, pos, loop_end); });
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
            is_playing = false;
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

    class PlayerAsyncTask : AsyncTask
    {
        PlayerView view;
        OggStreamBuffer vorbis_buffer;
        AudioTrack audio;
        long loop_start = 0, loop_length = int.MaxValue, loop_end = int.MaxValue;

        static readonly int min_buf_size = AudioTrack.GetMinBufferSize(22050, (int)ChannelConfiguration.Stereo, Encoding.Pcm16bit);
        int buf_size = min_buf_size * 10;

        public PlayerAsyncTask(Activity activity)
        {
            view = new PlayerView (this, activity);
            audio = new AudioTrack(Android.Media.Stream.Music, 22050, ChannelConfiguration.Stereo, Android.Media.Encoding.Pcm16bit, buf_size * 5, AudioTrackMode.Stream);
        }

        public void LoadVorbisBuffer (OggStreamBuffer ovb)
        {
            vorbis_buffer = ovb;
            foreach (var cmt in ovb.GetComment (-1).Comments) {
                var comment = cmt.Replace (" ", ""); // trim spaces
                if (comment.StartsWith ("LOOPSTART="))
                    loop_start = int.Parse (comment.Substring ("LOOPSTART=".Length)) * 4;
                if (comment.StartsWith ("LOOPLENGTH="))
                    loop_length = int.Parse(comment.Substring("LOOPLENGTH=".Length)) * 4;
            }

            if (loop_start > 0 && loop_length > 0)
                loop_end = (loop_start + loop_length);
            int total = (int) vorbis_buffer.GetTotalPcm (-1);
            view.Initialize (total * 4, loop_start, loop_length, loop_end);
        }

        public bool IsPlaying
        {
            get { return audio.PlayState == PlayState.Playing; }
        }

        public void Seek (long pos)
        {
            total = pos;
            vorbis_buffer.SeekPcm(pos / 4);
        }

        protected override void OnCancelled()
        {
            if (IsPlaying)
            {
                stop = true;
                audio.Release();
            }
            base.OnCancelled();
        }

        bool stop;

        protected override Java.Lang.Object DoInBackground (params Java.Lang.Object [] @params)
        {
            return DoRun ();
        }

        long total = 0;

        Java.Lang.Object DoRun()
        {
            var buffer = new byte[buf_size / 4];
            audio.Play ();
            while (!stop)
            {
                long ret = 0;
                ret = vorbis_buffer.Read(buffer, 0, buffer.Length);
                if (ret <= 0 || ret > buffer.Length) {
                    stop = true;
                    if (ret < 0)
                        view.Error ("vorbis error : {0}", ret);
                    else if (ret > buffer.Length)
                        view.Error ("buffering overflow : {0}", ret);
                    else
                        view.ProcessComplete ();
                    break;
                }

                if (ret + total >= loop_end)
                    ret = loop_end - total; // cut down the buffer after loop

                if (++x % 50 == 0)
                    view.ReportProgress (total);

                // downgrade bitrate
                for (int i = 1; i < ret / 2; i++)
                    buffer [i] = buffer [i * 2 + 1];
                audio.Write(buffer, 0, (int) ret / 2);
                total += ret;
                // loop back to LOOPSTART
                if (total >= loop_end)
                {
                    view.ProcessLoop (loop_start);
                    vorbis_buffer.SeekPcm (loop_start / 4); // also faked
                    total = loop_start;
                }
            }
            view.ProcessComplete ();
            return null;
        }
        int x;
    }

}
