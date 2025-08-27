using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models
{
    public partial class CommunityFriendRequest
    {
        public int RequestID { get; set; }
        public int RequesterID { get; set; }
        public int ReceiverID { get; set; }
        public DateTime SentAt { get; set; }
        public string RequestStatus { get; set; } = null!;
    }
}