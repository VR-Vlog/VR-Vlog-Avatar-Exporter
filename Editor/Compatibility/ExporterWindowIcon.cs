using System;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The VR Vlog app icon (128px PNG, rounded corners) shown in the header at
    // the top of the exporter's windows (ExporterWindowHeader, and the header of
    // アプリに転送). It is embedded because the package ships source files only,
    // not image assets.
    public static class ExporterWindowIcon
    {
        private const string TextureName = "VRVlog.ExporterWindowIcon";
        private static Texture2D texture;

        // One texture shared by every window. It is not an asset, so it outlives
        // script reloads; pick the existing one up again instead of making another.
        public static Texture2D Texture => texture != null ? texture : texture =
            Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(item => item.name == TextureName) ?? Create();

        private static Texture2D Create()
        {
            var created = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = TextureName, hideFlags = HideFlags.HideAndDontSave };
            created.LoadImage(Convert.FromBase64String(Png), true);
            return created;
        }

        private const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAIAAAACACAYAAADDPmHLAAAOI0lEQVR42u2dX2xT1x3Hv75OAoRbO9frbkoVqJ29eIWSuJ1EJ0EaZ9JmaSEQqAbaWpxbTVMf" +
            "1hJnD5SXljdUaaIMtVL3p5CUTqVaISJiEtM24kbwgLTJXpVq2bTmOiR0iqX630yaBHLZQ3pO772+sR03OPden59kCf+7OOf3+f7O75zzu+c48GDsPpg9CHOY" +
            "+YLM6RaEwcEcX9sgOJjjaxsEB3N8bYPgYM6vbQgczPm1DYGDOb+2IXAw59c2BA7m/NqGwMGcX9sQOJjzaxsCB3N+bUPAsfZgNDD117DfWQRgJDD117LvWQRg" +
            "FDD117L/WQSocWMAsC6Ahf9atrpa+mNFUUQwGIQgCACARCKBdDqNbDaLVCqFZDLJALCrHTt2DJIkweVywel0AgCWlpagKAry+TwymYzmIcsystms7SGxfRfQ" +
            "3MDjV+/+DsFgENlsFoqiaJMg7qs0qLGxkb6mh2Rubg6pVEoDSTqdxtTUFNLpNBKJBIXEkZnD7GKeAWAGu3DhAoLBINLptMbZRqaHwwgSAooaEgJKPp+HoigU" +
            "EBI9pqamcOnSJVNGD1sDEAqFMDQ0RJ1v5OBSUJQLiVE0Ic+dTidkWUZPT4/pIHACOGFXAN588008/PDDWFxchMPhwObNm7Fx40bU19ejoaEB9+7d0zj2/v37" +
            "BQ+HQ7te5nA4VnyobWFhgT7m5+exZcsWzM/P48aNGywJrJb6d+7ciVwuRxV5/fp1yLIMn8+HpqYm+uA4TpMc6vv+1apf/1xRFCwtLaGpqYmNAqplfX191GEc" +
            "x2FmZgZHj/xUk5w1N/C439QIn88HQRDgdrvh9XrR1NRkCAnP86ivry8LEj0QTqcT8XicAVAt9Xd0dFD1u91unDp1qiAzn13MA8l80X6ZQOLxeCgggiDgscce" +
            "gyAIGkg8Hg94ngcADRQcx0GWZUSH/0ivaZZRQl0tqF+WZVy6dKmia+khuXnz5oqTTB6PBwCwb98+vPTSS8jnl50sCIIGQG9gO/o6O/H666+ve1vZbi1g165d" +
            "6OjooI3vdrsxPDz8wLPvZDKJiYkJTExMwOfz0S7ACMDOzk4MDAzA7/czANbaIpFIQeg9d+5cVbufnp4eTfejBrC5gUdvby8FgQGwxurfvXv3io1fre6nGIC9" +
            "LzyHbdu2YWlpCW63mwGw1upXh950Ol1V9ZcCsLmBR19fH50xjEajDIAH2fjnzp2rqvolSSoKIFE/mZNYKaFkADyAxq8GgN3d3UUBJOrnOA6Dg4OmaDfOLurv" +
            "7u6ueua/GgAPHTqEbdu2AQA+/vhjXL16lQGw1o2vKApt/DfeeKOqAHZ1dRVVvyRJyOfz4Hm+qpHJ9gD4/X50dXWtu/rJDOBK6t+5cydV/wcffMAAWCvr7+8H" +
            "z/NU/dlsFkNDQ6YC0KzqtzwA+sbneR4XL17ExMSEaQBUq//WrVt0PYABsAYWDoc1jZ/P59dd/XoA1eofHBw0XakYZ2X1Hzx4cF3VrwdQr35Sk0DUX+mCFAPA" +
            "pOovBWB/fz9Vf7UTU1sDYNT4165dW1f16wFUqz+Xy5ku+bM0AOFwGG63W9P4p0+friqAvb29RQEks35mVr8lARBFEb29vchms+uqfkEQVgSQVCQpioJcLlfV" +
            "rsn2AEQiEdr4wHINXjXDazkAEvW7XC4MDw9XFU5bA6BvfJfLhStXrlR1VU0PYD6f1wBIKpLILWdmVr/lAJAkyVTqd7lcuHbtmgbASCQCRVHoe2ZWv6UAEEUR" +
            "kiStq/pLAUhqEkjBh1kzf0sCYAb1lwKQ1CPyPF91OG0NgFHorXZFzWrUD8AS6rcMAJIkwefzaW7NquZ6fzkASpJE1W+Wci9bAGA07BobG6u6+tUAKoqiAVBd" +
            "kWSmci9bAHDgwAFN41e7gctVP8dx4DjOVOVelgeAlFHr1V/NBtYDCEADIFkSzuVy4Hm+qlPStgeg94Xn1lX95QBICkKsqH5TA2AG9ZcCUK1+UghqNeOY+tdG" +
            "/erbvxkAD6Dxqx1eV6t+M5Z7WRaAzt4fahq/vr6+qslVOQCSghCi/uGz78GKZkoAIpGIpvFjsdi6q18NIKlIsrr6TQnAoUOHChq/2slVX18f7t69W7b6zVjs" +
            "Wa6ZbosYSZJo45Nqm0gkAkmSqrKNKwEwnU7T7kcNICkHy+VyEAQB77zzjqW3j60zm/oDgUDBrp4ejwctLS2ajReByrZxLeUs9Yof6X7Ut3KFw2G4XC7kcrmq" +
            "34FsewDU6tcbWWUz7Mc4Dh6PBx6Pp2AbVzUkZBvXRCJhCEkgEMCOHTvo/6Xvfsi0cC6Xg8vlwtDQkOU3j64zk/rJxo5G27eWu8/vSqCQufpyogn5vP5GTrIk" +
            "nE6nLVHuZSkAJEkquhNnMceTnT6LRQn154tBQkw/s6cuCHG5XBgZGTF9uZdlRgHkJgqynLoa55ObM8bGxjA5OVn6D/4yEhg91J+RZdlQ/QQgqy36mBqA/v7+" +
            "VaufOH98fBwdHR04fPgwwj3PUggqiSbke2Rsr+/7ifqtUOxpGQAqVT8ZIh45coQmYmQyhhz8UFGDGMzsqQtCrFLsaRkA2traKv5uIpHQZOEvvvgizeJXew6A" +
            "Xv0EJrX6rVbuZZkkkGTixY5zMXJWa2srTp48iXg8jmAwiO7ubszNzVXsfCP1k4IQMjFkpXKvcmzdTwwRRRFjY2MQBIHOAZSzRz9xMs/zcDqd9MiWSk1RFAiC" +
            "gNdeew1vv/02gOVFoctjf6HzC9evX8fhw4dtBcC6R4BkMon9+/cjHA6XPMhhaWmJOotAQnbmKtanl+N8l8uFWCymUT9ZFCIzk3ZTvykiwEpRgezPX+5BDsUm" +
            "dUqNCgRBgCzL6Ovro9m9KIoYGRmh6h8fH0coFLIdAKY8LyCZTJacYlUf5OD1eiks6oMcvF4vANCDHPRTxE6nE9lsFsPDw3j11VeRTCbpYQ4k8yfqt8u439QR" +
            "IBQK0dW99OTM115jL3baBwBMTU0hGo1S1RPn79q1C+fPn9fMNdhR/aYD4OTJk3j55ZcxPT1dcJInWbjRLwGv9SGN6tBPADh48KCthn6mzgEuXLiAvXv3IpvN" +
            "Fpzipe7PyRIwAM0hjeTf2WwW6XQasizTbqWU+f1+nD59ms4lCIKAs2fP4vjx47CrmTIJPHbsGAKBAE30SD9Ohn3qhE9v+pGC+iRPfUEJiSRutxvBYBBdXV2a" +
            "IeHo6Kjthn2WAKDcflx/ahcZFax0tKt+OKmeACKLPBzHwe12Y3R0tOCYOQaAiU0/dCQjg5WGjuqjXdUwzMzMYHBwkE4G2d1sf3h0OUNHALQqKBH7xPaqr1kA" +
            "mBUax5qAAcCMAcCMAcCMAcCMAWD7YWAl7xX7jP61cq5jJjPt8fG/2XoAYeEp7EsM4Wru3/T1D1t/gr384+ic/DV+Ie7B7k1ezfcmF1P42e1LGJ+bLXDM0MiH" +
            "aGpqQk9PD10bEEUR7777LoDl6mSy7KsuNgWWF6qCwaDhbz169ChkWcbIyIjh+4lEwrRTyqaNALfuZdBQ58YzvE/jxL3849TRjzpdaK5fXiNIK19AcDbiuw9t" +
            "xy+3/LDgerOLeSQSCTzxxBN48skn6es+nw979uxBJpNBKpVCa2srrSNQmyAI8PmWfwtZUyCzjMTI7WnqdQfWBVRof/3ffwBlAc9sbqWvBTY+igZuI0bvfKqZ" +
            "reub/gO+/c9T+PHM+4CyABe3wfCa8XgcgLYSORAIAACi0SiSySRdPDIynudx4sQJhEIhhEIhej0AcGSWq49SqRTCPc/Szzz99NOmXlAyLQCTiynM3k3hqU0t" +
            "tF/9zuYWgNuA2Be3Db9z595i0WvGYjHk83nqdABob2+n732d/vt+UyOsaKYFYHYxj/j8f9FQ50Zg46MAgN2Ny6H5o7xc5C/asOJbidgnkGUZ7e3taG7g0dzA" +
            "o729HdPT00jEPqFOJEvQRuZ2u9kooFp29c6/vlI+gPaNW7B4L4vY/GeVQxWPY+vWrRBaWyC0tsDn8yEej3/tBSDSBaijgSiKtCtgAFRgN/PTgLKA9k1bsKOx" +
            "Gc31Hvz9i5VrBeeUu4CyUPSa6jygra0NPM8jGo1W/BtJgYpRF+Dz+XDx4kWcOHGCDQMrzQMWlXls39CM/e7tALcBH90pvAP4Gd6HrDKPH31jZ8lrkjwgGAzS" +
            "u31isVjFv7HYzSik6sjMowFTAzC7mMfonU/xg4e24+eeTYCyYNj/vyJ+H688speq3wgSfR7Q2dmJTCaD6elpWjdILJPJaEK63qmlugD1/EElt6mxLkBl1+cS" +
            "y+NwZyNm76YM+//ffn4Db33+ZwDAxMJtHP/sTyXzAJ7n0dLSgng8blgw6g1sh9/vh9/vhyiKVO1tbW0QRRF+v1+TLKoTyFAoBFEU4fV6K75NnUWAL+1vd2YA" +
            "AA11bkzq+n+hbhPAbcB76RjG8jK+1/gt+De14qBnBy6mxovmAc8//7xh+Od5HoIg0Fk9nudx5swZ+p2BgQEMDAwAWK4tJJ+/efMmJicnsWfPnoKtY4qNKhgA" +
            "pfrs+c/wVnoMjzh5vJ/5h+a932fiaF+YxV0sF3lGbl9B/zd3Y2tD8aFaNBrFmTNnAACXL1/WvHf+/HnNc0EQEI/H6W4h5KYSTbeSWI5SR44cgSRJBQ43cw7A" +
            "SsJq3BykC2NNUZv+Z/UANW4MANYFfDWSYc1Re75nEYBRoDEWBWrM7ywCMBIKjEWBGvI5iwCMBkNjUaBG/O0o8mEGQQ2I3VHiSwwCe0f6kgAwCGzs/HIBYBDY" +
            "1PmrAYBBYEPnrxYABoHNnF8JAAwCGzm/UgAYCDZw/FoAwECwsOPXEgAGggUd/yAAYDBYxOlq+z93UAt2M3UgAAAAAABJRU5ErkJggg==";
    }
}
