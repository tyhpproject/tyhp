using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Tyhp.Config
{
    class ActionConfigSource : IConfigurationSource
    {
        protected string TyhpProjectFilePath {get; set;}

        public ActionConfigSource(string tyhpProjectFilePath)
        {
            this.TyhpProjectFilePath = tyhpProjectFilePath;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ActionConfigProvider(this.TyhpProjectFilePath);
        }
    }
}