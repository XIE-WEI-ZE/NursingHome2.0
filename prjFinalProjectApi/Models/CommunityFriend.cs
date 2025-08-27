using System;
using System.Collections.Generic;

namespace prjFinalProjectApi.Models
{
    public partial class CommunityFriend
    {
        public int MemberID1 { get; set; }
        public int MemberID2 { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
