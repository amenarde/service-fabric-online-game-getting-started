// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    public static class Partitioners
    {
        public static int GetRoomPartition(string roomid)
        {
            return roomid.GetHashCode(); // Partition key range has been set to the range of Int32
        }

        public static int GetPlayerPartition(string playerid)
        {
            return playerid.GetHashCode(); // Partition key range has been set to the range of Int32
        }
    }
}