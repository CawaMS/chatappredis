using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace chatapp;

public static class Utility 
{
    public static string GetTimestamp() => DateTime.Now.ToString("yyyyMMddHHmmssffff");
}


