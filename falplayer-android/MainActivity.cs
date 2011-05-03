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
            Stream input = File.OpenRead("/sdcard/ED6437.ogg");// Assets.Open("ED6421.ogg");
            //button.Text = string.Format("file size: " + Assets.OpenFd ("invincible.ogg").Length);
            var vorbis_buffer = new UnmanagedOgg.OggStreamBuffer(input);
            button.Text = string.Format("bitrate: " + vorbis_buffer.GetBitrate(-1));

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

        static readonly int min_buf_size = AudioTrack.GetMinBufferSize(44100, (int)ChannelConfiguration.Stereo, Encoding.Pcm16bit);
        int buf_size = min_buf_size * 10;

        public PlayerAsyncTask(Activity activity, Button button, OggStreamBuffer ovb)
        {
            this.activity = activity;
            audio = new AudioTrack(Android.Media.Stream.Music, 44100, ChannelConfiguration.Stereo, Android.Media.Encoding.Pcm16bit, buf_size * 5, AudioTrackMode.Stream);
            this.button = button;
            vorbis_buffer = ovb;
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
            var buffer = new byte[buf_size];
            long total = 0;
            long buffered = 0;
            do
            {
                long ret = 0;
                ret = vorbis_buffer.Read(buffer, 0, buffer.Length);
                if (ret <= 0)
                    break;
                audio.Write(buffer, 0, (int)ret);
                if (buffered < 0x10000)
                {
                    buffered += ret;
                    if (buffered >= 0x10000)
                        activity.RunOnUiThread(delegate { audio.Play(); button.Text = "play started"; });
                }
                total += ret;
                //activity.RunOnUiThread(delegate { button.Text = total.ToString(); });
            } while (!stop);
            activity.RunOnUiThread(delegate { button.Text = string.Format("total: {0} bytes", total); });
            return null;
        }
    }

}

