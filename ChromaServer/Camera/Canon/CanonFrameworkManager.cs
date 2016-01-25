using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Canon.Eos.Framework;

namespace ChromaServer.Camera.Canon
{
    public sealed class CanonFrameworkManager
    {

        private EosFramework _framework;

        public event EventHandler CameraAdded;

        public IEnumerable<EosCamera> GetCameras()
        {
            using (var cameras = _framework.GetCameraCollection())
                return cameras.ToArray();
        }

        public void LoadFramework()
        {
            if (_framework == null)
            {
                _framework = new EosFramework();
                _framework.CameraAdded += this.HandleCameraAdded;
            }
        }

        public void ReleaseFramework()
        {
            if (_framework != null)
            {
                _framework.CameraAdded -= this.HandleCameraAdded;
                _framework.Dispose();
            }
        }

        private void HandleCameraAdded(object sender, EventArgs eventArgs)
        {
            if (this.CameraAdded != null)
                this.CameraAdded(this, eventArgs);
        }
    }
}
