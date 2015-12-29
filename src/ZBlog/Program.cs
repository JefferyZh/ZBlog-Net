﻿using Microsoft.AspNet.Hosting;

namespace ZBlog
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var application = new WebApplicationBuilder()
                .UseConfiguration(WebApplicationConfiguration.GetDefault(args))
                .UseStartup("ZBlog")
                .Build();

            application.Run();
        }
    }
}