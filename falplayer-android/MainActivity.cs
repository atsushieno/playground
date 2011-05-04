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

            // Get our button from the layout resource,
            // and attach an event to it
            Button button = FindViewById<Button>(Resource.Id.MyButton);
            Stream input = File.OpenRead("/sdcard/ED6437.ogg");
            var vorbis_buffer = new UnmanagedOgg.OggStreamBuffer(input);

            player_task = new PlayerAsyncTask(this, button, vorbis_buffer);

            button.Click += delegate {
                try {
                    if (player_task.IsPlaying)
                    {
                        player_task.Cancel(true);
                        button.Enabled = false;
                        return;
                    }

                    player_task.Execute();
                }
                catch (Exception ex)
                {
                    button.Text = ex.Message;
                }
            };
        }
        PlayerAsyncTask player_task;
    }

    class PlayerAsyncTask : AsyncTask, SeekBar.IOnSeekBarChangeListener
    {
        Button button;
        SeekBar seekbar;
        OggStreamBuffer vorbis_buffer;
        AudioTrack audio;
        Activity activity;
        long loop_start = 0, loop_length = int.MaxValue, loop_end = int.MaxValue;

        static readonly int min_buf_size = AudioTrack.GetMinBufferSize(22050, (int)ChannelConfiguration.Stereo, Encoding.Pcm16bit);
        int buf_size = min_buf_size * 10;

        public PlayerAsyncTask(Activity activity, Button button, OggStreamBuffer ovb)
        {
            this.activity = activity;
            this.button = activity.FindViewById<Button>(Resource.Id.MyButton);
            this.seekbar = activity.FindViewById<SeekBar>(Resource.Id.SongSeekbar);
            audio = new AudioTrack(Android.Media.Stream.Music, 22050, ChannelConfiguration.Stereo, Android.Media.Encoding.Pcm16bit, buf_size * 5, AudioTrackMode.Stream);
            this.button = button;
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
            button.Text = string.Format("loop: {0} - {1} - {2}", loop_start, loop_length, total);
            // Since our AudioTrack bitrate is fake, those markers must be faked too.
            seekbar.Max = total * 4;
            seekbar.SecondaryProgress = (int) loop_end;
            seekbar.SetOnSeekBarChangeListener (this);
        }

        public bool IsPlaying
        {
            get { return audio.PlayState == PlayState.Playing; }
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

        protected override Java.Lang.Object DoInBackground(params Java.Lang.Object[] @params)
        {
            try {
                return DoRun ();
            } catch (Exception ex) {
                activity.RunOnUiThread (delegate { button.Text = ex.Message; });
                throw;
            }
        }

        long total = 0;
        int loops = 0;

        Java.Lang.Object DoRun()
        {
            var buffer = new byte[buf_size / 4];
            audio.Play ();
            while (!stop)
            {
                long ret = 0;
                ret = vorbis_buffer.Read(buffer, 0, buffer.Length);
                if (ret <= 0) {
                    stop = true;
                    activity.RunOnUiThread(delegate { button.Enabled = false; });
                    break;
                }
                else if (ret > buffer.Length) {
                    activity.RunOnUiThread (delegate { button.Text = "overflow!!!"; });
                    stop = true;
                    activity.RunOnUiThread(delegate { button.Enabled = false; });
                    break;
                }

                if (ret + total >= loop_end)
                    ret = loop_end - total; // cut down the buffer after loop

                if (++x % 50 == 0)
                    activity.RunOnUiThread(delegate {
                        activity.RunOnUiThread(delegate { button.Text = String.Format("looped: {0} / cur {1} / end {2}", loops, total, loop_end); });
                        seekbar.Progress = (int)total; 
                    });

                // downgrade bitrate
                for (int i = 1; i < ret / 2; i++)
                    buffer [i] = buffer [i * 2 + 1];
                audio.Write(buffer, 0, (int) ret / 2);
                total += ret;
                // loop back to LOOPSTART
                if (total >= loop_end)
                {
                    loops++;
                    activity.RunOnUiThread(delegate { button.Text = String.Format ("looped: {0} {1}", total, loop_end); });
                    vorbis_buffer.SeekPcm (loop_start / 4); // also faked
                    total = loop_start;
                    seekbar.Progress = (int) total;
                }
            }
            activity.RunOnUiThread(delegate { button.Enabled = false; });
            return null;
        }
        int x;

        public void OnProgressChanged(SeekBar seekBar, int progress, bool fromUser)
        {
            if (!fromUser)
                return;
            total = progress;
            vorbis_buffer.SeekPcm (progress / 4);
        }

        public void OnStartTrackingTouch(SeekBar seekBar)
        {
        }

        public void OnStopTrackingTouch(SeekBar seekBar)
        {
        }
    }

}

