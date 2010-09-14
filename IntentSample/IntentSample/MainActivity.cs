using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

namespace IntentSample
{
    public class MainActivity : Activity
    {
        int count = 1;

        public MainActivity(IntPtr handle)
            : base(handle)
        {
        }

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Set our view from the "main" layout resource
            SetContentView(R.layout.main);

            // Get our button from the layout resource,
            // and attach an event to it
            Button exp = FindViewById<Button>(R.id.explicitButton);
            exp.Click += delegate {
                Intent intent = new Intent ("org.openintents.action.PICK_FILE");
                StartActivityForResult (intent, 1);
            };
            Button imp = FindViewById<Button>(R.id.implicitButton);
            imp.Click += delegate
            {
                Intent intent = new Intent();
                intent.SetDataAndType(Android.Net.Uri.Empty, "*/*");
                intent.SetAction(Intent.ActionPick);
                intent.AddCategory(Intent.CategoryOpenable);
                StartActivityForResult(intent, 2);
            };
        }
        protected override void OnActivityResult (int requestCode, int resultCode, Intent data)
        {
            base.OnActivityResult (requestCode, resultCode, data);
            if (resultCode == (int) Result.Ok && data != null)
            {
                switch (requestCode)
                {
                case 1:
                case 2:
                    String filename = data.DataString;
                    if (filename == null)
                        return;
                    if (filename.StartsWith ("file://"))
                        filename = filename.Substring(7); // remove URI prefix
                    Button exp = FindViewById<Button>(R.id.explicitButton);
                    exp.Text = "Selected: " + filename;
                    break;
                }
            }
        }
    }
}

