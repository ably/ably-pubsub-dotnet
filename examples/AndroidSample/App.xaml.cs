using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xamarin.Forms;

namespace AndroidSample
{
    public partial class App : Application
    {
        private readonly AblyService _ably;

        public App(AblyService ably)
        {
            _ably = ably;
            InitializeComponent();

            Current.MainPage = new TabbedPage
            {
                Children =
                {
                    new NavigationPage(new MainPage(_ably))
                    {
                        Title = "Main",
                    },
                    new NavigationPage(new LogPage(_ably))
                    {
                        Title = "Log",
                    },
                }
            };
        }

        protected override void OnStart()
        {

            // Handle when your app starts
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
}
