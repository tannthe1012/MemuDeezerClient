using System;

namespace BoosterClient.API.Exceptions
{
    public class APIException : Exception
    {
        public string Title { get; set; }

        public string Detail { get; set; }

        public int Status { get; set; }

        public APIException() { }

        public APIException(string message) : base(message) { }

        public APIException(string title, string detail, int status): base(title)
        {
            Title = title;
            Detail = detail;
            Status = status;
        }
    }
}
