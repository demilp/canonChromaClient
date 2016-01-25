using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChromaClient
{
    public class PhotoInfo
    {
        private String _id;
        private String _backgroundPath;
        private String _foregroundPath;
        private String _description1;
        private String _description2;
        private Boolean processing;

        public PhotoInfo() { }

        public PhotoInfo(String id, String backgroundPath, String foregroundPath, String description1, String description2) 
        {
            processing = false;
            
            _id = id;
            _backgroundPath = backgroundPath;
            _foregroundPath = foregroundPath;
            _description1 = description1;
            _description2 = description2;
        }

        public String Id
        {
            get { return _id; }
            set { _id = value; }
        }

        public String BackgroundPath 
        {
            get { return _backgroundPath; }
            set { _backgroundPath = value;}
        }

        public String ForegroundPath
        {
            get { return _foregroundPath; }
            set { _foregroundPath = value; }
        }

        public String Description1
        {
            get { return _description1; }
            set { _description1 = value; }
        }

        public String Description2
        {
            get { return _description2; }
            set { _description2 = value; }
        }

        public void markAsUsed()
        {
            this.processing = true;
        }

        public Boolean isUsed()
        {
            return processing;
        }

    }
}
