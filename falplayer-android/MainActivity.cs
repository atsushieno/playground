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

    class PlayerAsyncTask : AsyncTask
    {
        Button button;
        OggStreamBuffer vorbis_buffer;
        AudioTrack audio;
        Activity activity;
        int loop_start, loop_length, loop_end;

        static readonly int min_buf_size = AudioTrack.GetMinBufferSize(22050, (int)ChannelConfiguration.Stereo, Encoding.Pcm16bit);
        int buf_size = min_buf_size * 10;

        public PlayerAsyncTask(Activity activity, Button button, OggStreamBuffer ovb)
        {
            this.activity = activity;
            audio = new AudioTrack(Android.Media.Stream.Music, 22050, ChannelConfiguration.Stereo, Android.Media.Encoding.Pcm16bit, buf_size * 5, AudioTrackMode.Stream);
            this.button = button;
            vorbis_buffer = ovb;
            foreach (var cmt in ovb.GetComment (-1).Comments) {
                var comment = cmt.Replace (" ", ""); // trim spaces
                if (comment.StartsWith ("LOOPSTART="))
                    loop_start = int.Parse (comment.Substring ("LOOPSTART=".Length));
                if (comment.StartsWith ("LOOPLENGTH="))
                    loop_length = int.Parse(comment.Substring("LOOPLENGTH=".Length));
            }
            loop_end = loop_start + loop_length;
            button.Text = string.Format("loop: {0} - {1}", loop_start, loop_length);
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

        Java.Lang.Object DoRun ()
        {
            var buffer = new byte[buf_size / 4];
            long total = 0;
            audio.Play ();
            do
            {
                long ret = 0;
                ret = vorbis_buffer.Read(buffer, 0, buffer.Length);
                if (ret <= 0)
                    break;

                if (ret + total >= loop_end)
                    ret = loop_end - total; // cut down the buffer after loop

                // downgrade bitrate
                for (int i = 1; i < ret / 2; i++)
                    buffer [i] = buffer [i * 2 + 1];
                audio.Write(buffer, 0, (int) ret / 2);
                total += ret;

                // loop back to LOOPSTART
                if (ret >= loop_end)
                {
                    activity.RunOnUiThread(delegate { button.Text = String.Format ("looped: {0} {1} {2}", total, loop_end, vorbis_buffer.TellPcm ()); });
                    break;
                    vorbis_buffer.SeekPcm (loop_start);
                    total = loop_start;
                }
            } while (!stop);
            
            return null;
        }
    }

}

