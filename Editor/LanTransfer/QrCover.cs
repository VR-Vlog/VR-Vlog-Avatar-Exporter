using System;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // What the QR area shows until "QRを表示" is pressed: three finder squares and a random pattern
    // on a 37-module grid, two fifths of it dark, blurred by 0.85 module (180px PNG, grey). A fixed
    // picture, not made from any link and not a QR code, so nothing on screen can be read before the
    // user shows the QR. Embedded because the package ships source files only, not image assets.
    internal static class QrCover
    {
        private const string TextureName = "VRVlog.QrCover";
        private static Texture2D texture;

        // Not an asset, so it outlives script reloads; pick the existing one up again.
        internal static Texture2D Texture => texture != null ? texture : texture =
            Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(item => item.name == TextureName) ?? Create();

        private static Texture2D Create()
        {
            var created = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                { name = TextureName, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            created.LoadImage(Convert.FromBase64String(Png), true);
            return created;
        }

        private const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAALQAAAC0CAAAAAAYplnuAAAvIElEQVR42u1925IjOa4kbiRDyure8/+feboyFUEC8H0AlXXLrO7ttV2ztZ2eeejRpESKYhCAw93JoP/3" +
            "/hH6z6T/M+n/TPo/k/43/9gHr318CvL/8bn843EZP70NH3wCf/IJ+PYiv7+AD177zQz5p//90bj808D2w+eAAAI+fDN/m8j+BIBQ/8e3kX99jfDRRH7+u9+Mu/+GP5w0" +
            "CEgkEvh5fZiImfk5b2bi+msQEbOwEDGBnu/+4TUA363rr+9loj3yx+MKCwsx+INJg5CZERkJ/PybMrPInjYzMzMoMxIgZlGFMBGQGfWaiDxfQ373ecwsTITMQNZ7RZiQ" +
            "+fm4KqoQ+W7W3690Zvhy98j85RuLiIoIc81fiBDhkUkiaqZaQ7t7BEjEzIx4T+f985hZlBmIcM8kVjVVoYz4dFw1s2ZE8sFKA0j3ec25Vsavb66PF2ZmURWmdJ8eSWzW" +
            "WjMVQviaa0WwWusdUKYMDw8AtbNEpN675vIgMevNhNLXJ+OqtNZ7Avbd0/jdSmfGPB+P81qeoB+ffmazZqYiJKJqyhRrXWslibU+ejMhxLqua65g6yNq90ZMX5EAExOJ" +
            "iJkyYl3nNZ2kjWM0pVjn+fG4Ym0cNxAL60d7GrHm4/X17XF5gum755rAor21ZiLMqs1MEHOecwVJa2P13oTJ5/l4XDO5HUEiTARf15oO1CMoYokU+HW+vZ0L2g+PYeTX" +
            "4+2TcW3cVpKIyK97GkCGX+frX1/frpXE/O0cA0CivfVuKixirTVBzOsxZ016jt5U4PPx+jhnygjWZkqUa57X8txPr0iCIFjzfHt9u1LHTGQjf7x+fX07/YNx25hBamYq" +
            "eO6P71c6fT7evv732xnJwu8bH4kEa+t70mq9NSGf13mtINF2Xb03FYrr8fo4J8y5DY8gxDzPczmRiDCzCDGTZvi6Hq9nyHAiBK3H69e/PhxX9HDS1kd+d7J8v6fD1/V4" +
            "e3u9gliFn2lJIgMQ662bCYu01htTzHlND2Kxs7dmKhzz8fa4nBp3j0QS1nWd1woWMIiRFCJMiHCf15vzTGYOXm9vr69vH41LGmT9WB5JH54eiLXmeV4zRIj4GbOAiEyK" +
            "cDcTErU2G3OsNT0SxItPVRPhnNfjXCHkCSIkxVprrgXhiiJMyGBCAhk+JxyiErzO8zzPmR+Mq6R9rhUAwJ+f0xGorYXn88NMgAMIFxIxX8acHp4JUHruuJU+rxlkHcRM" +
            "hIzYHydqKgQCZTAYYGakR5Aph/h5zeX50bgR7ssjE/RpRMwdp1QIFe+UK9gRgpHMLBFqTEgkMXEivMIgIZYHhJOYmZAUHgkWMeumQoFMgIRBrCqc7iTMoXHN+ua/jkvv" +
            "08LHuUd9R1ZWVUJGJImyPk9IJIGJOVKdicDMwmCKXCs8CcgI4mYgImQQeSREVVprpgQKJDhFiUTNBJ5JxGGYHmBh+2Vcxl56widZXqUwSqzKiLVWkjSQsRIItN8toBAm" +
            "JlFRoaRkxJyemUiwcWRm+BJFeJIwa7OmQhnIlSRmAlZToVgLxNHJPYlVfh2XoDuj+k0RUMeSEHxe1wzSPkBGovU2MBiUlEwsTCJGYKhQxnLPBEmKrzWvB4dyZpKyqJkK" +
            "IWOt6RDrJiSiQrmQYLimg4Tk13EJIsx/U7kwi4AJOc/H2+VkI0AsxJKMOgCYQJQkJMRqDEGGL0ZUrgRp1/lm8G7ClW2oMoLC5zUvh7TsDSTMFCscQmmUqGfwl3Hp7ydd" +
            "iScl/Hp7/XouakeCxZhYwLLDFQO1/UVVKfYx4ZQZJCTaXoXW2ZuZNhNVoQTS/ZrX5Sk96yMIGSu4qRAD9OG439L43610PXExr8fr17dFzVlMSZmISVjkmezv30WV9ZkP" +
            "RwQliEUZ6xy99z7ACkLWybVXOsAkngAQkeoeIVWo4ZdxhT8oEz8sbJHh83q8vU3qZH10rdAgKspMoMjInUyYfEv2M5Oyzr7rMUY/bsmiGpTuy33Nua6AJDHpPid3xYR9" +
            "PP0yLv/TarzOLve1FvFyj51aipqZCCHDOYlZRESUlSuUZEZEVmZ+tTHGSjYzDsRa01esNVdAQATNuRJVlqiK1GP+4bj/cNIfFvLMYr11VcpcvhxsKjVtqZIpwhcjMzN8" +
            "2jWO5NabgtPnXO7hy1eAMxGGuYLEWEdvZlUS8/8G7lGhyaw1UGu7liIWtXaMppy+pgio1kiFVZQJsaYKMkiAcF0O7deYkhRrTvfIiLUyKSKWkk8nbabjGM2QYK4n+8dx" +
            "//H2YBK1Pm5Ojdr9NrqpCBGL9nHrxumXigZJMxURFVITynU2pSpjkaFJ2tfyJZEx1/KsctEDstZlHCtIO9s4RpOkJGKmX8blf7I9dpHDFJ6wRe14uY1uVcur9WMY5xIh" +
            "SYh1MzMVQSriGk0YGQQQBKzu7muxhFc2RPV1Ajy1fhNTaeMYRjvx/HVc/gjvsZ/nDAAsxAmW4WTjfhtN9rFsrXWTYCA5Saw1a5W/KXx0U6bKoohIIiIynNjXWglmRuW/" +
            "AZb67Uy0j6MJwEQg4Y/GBX6etf16cCSIWTuxjiDtY3QlJHjDFMoQEQVIa9JqIjBax2gm/PziyEwg0wk+pyeJyj4gVhCJmqqq9d6HcgZRAqS/jFuwCf39pINJ2Yi1JUlr" +
            "zRiZ4Kw4mPTMka31WmkVaB5j9GYqkjts7nMwcl7TkxXK2PlfgjU6t9ZH703hTECA+JdxA/T7Se90KoKJlJRYk0RNCREOUQDpEvA5l0Pqea1Jk+Y6jmOMPlG1s2odLpTh" +
            "a85kC1NCheVMAudGEBonLSZk4tdxA8/k8+NJF2BHQBCIhEQFxFzBJIgBZCxQzDlXkg0hVjUzVSHOdbvd7/fLOQqOamP01nTDIUHWmymxWGRmIhOs7bjVeWfCqNzgl3EF" +
            "9aN9CEBWRibCO6gyMYOYCAiPJCGijOmxzmt6yoB4Vs6kQoxx3O8vjyskIKKqbdxux2iWlD7PRea9N2Pbe5CIxPo4bk0Q8HbJM/ml+s8e931axB+uNIuqqQgBGST729Uj" +
            "RSwiTEGY19t5OexIbj0AgIiZrR+3l7fHleoQU7U+jpeX2xCfiHnOtBUkqqLKROlELNrG7dY0nWL15QFkvG+E/QiyqDVTkY9WmolF1Ky1FvEDDE1MQqStmVLCz7fXt9Op" +
            "OelGQpiFWVs/bveXK9VJmrXWx3G/H40upVjXGRZkXczIGVl1llgbR1cszvBIBAhZEHH9l7WOkdb0OxTn++3BhRv2FQzWb7k3khOsfXQT8nm+vb6eTh06rqubMhELs2jr" +
            "4zhuzsHW++h9HMetS0A5fZ6hpAHpjZ1iST13Yq0fhia1qh7YtWcBAJwkdqtdpvLxgyii1vrwlGQR/fY7FSzWuwnS57yuy4lszbXWEgKYWep3bK0jtd2O4xhjjG5YroxY" +
            "080S2vogycnIoIgEiXXjECKAeK7MQsCFmQkE1n57ud9G+yGg23vvhZnFrPW+QnKD3d+6D2JVUEdERCYKzc9wFSJmqUqeWMTQjpeX+22M0YzzvJQpMxNgtT460SSEr9C5" +
            "VoDYxPhZUayoJdg/NIuO28v9NrqJfNsfP5zTIqpqZgTWSrGepbuImiplRkHSLL2ZKQPhLBEOdnd3D5C2cf/yx/3o3QQrtB58rimPhmCEz8ncHuc1q15nIhCJ8IrcE5EK" +
            "BON+r+3xcc+lKpEK0Wxmyrz7ORW+hRLuHpDGpP35UcgIZ9Ca13VecyVLO+5fXm69CQWpMAurcu+1YYIRvq5JpON+v4/eVHnvwqymwS4/pZ7UY/Rm363zhzXiLllNhSt9" +
            "2p/ABZslaWts4/ZSz4cQMjgwH4/Xt7fHRUzaxnG/deOgKtOsDYz77XaMJomMNdeF5HYcR28mIkYZ9VM5IUFSh6yaNbOmP5543/DpPeNEpAds16fIfX6rMBLhawVJF2vj" +
            "dr8dozdRpgxQnm+vr1+/vi7hILbee1cCA0msfTAdLy+3yr0iItbMIG2tmapwE7HWe7+UCYHIZzfth97exyuNSsKWU7Ko7gy4MmnhEKS7B6lY7+M4jtF75XUR8Mfb169f" +
            "v766iUdFMU6Ee0LaeOl8fHm5DZOsAjjDPUlUK9oZs1qtKSGQFbOIOQrD/AF//Gl7ABm+5oIRC0MoExAmVlPirAQNYn2MPnprzZoQEpmxaqFfo2mBnBvpXU7SbhJyvHy5" +
            "dSVEZgJIR4Clvh11BbGaNVOpMhMoKE6mtu5u3yG9vxQBhRungphSKZMIUjkXhxaOyaK716WqQpkI9+t8PN4eb4/Mds253J0pruuaAR1o0HG7DxPKzAJV01eAWdVMGQ1J" +
            "otZ6ZEYiiFm4ps3azMw+ACCf+UgtdQhAaLqBH1a1Rp5WFe7ebrVzePcQ5pzXdV1XZruu6zo7B/n1OKdDuwRpH0dXQmZlWUwZQbWXjTEkQdpGEhFxgAmZ6UgQaWu9x4c9" +
            "l/ddHb6cQYwwJmIFiVprLJhVED4Ritpp1afdj76vwPl4e309NI39Oh/TSUcDSWsmTJV7qaoJZV5qfRyjCaUiwa1ycVtJ1U6NTJC2/mnP5fsdQsSMbFIItIhZIw57ZlvY" +
            "yExkEPa/1U5Nhz1ev94arUYx57WCVIiItWDhnbf1ZhoR83y8fb0N5TQBWLuYqV0zCnVHIhLaj+WZfwdAAi4qDGWlCs4qTPWkM95RsAgXYHeIUW2VxPU2jq55NUlfK5OV" +
            "iUCMIBHU79b7WAlminWdb7cm2VSY2dTU2pgBEqb0dEpq1/TMD3ouTziNK4SCEOEEoSqhf8Yo+fmbLEl3910OmDLFfLRuHFdXikgieWboGaoEFmv9WJ7ECyrINc+utExV" +
            "lUVEbXomEcLTM5Jntc0/X+nK1RKk8i2yZngIR9VJT+KBqlA6KNw9EIUoNOVcD1GhmKMJKm1lQnoki5kQqfXDA6xtBbcmlD4vNlNrUBVmtUJU1+kUAYlq7322p2vOPThJ" +
            "zUT2Dndfk8T3NiBmVWutmVBEruUBojpnzRieJELp9261n4QJ6StIrJkQW/cgsXEuh/RujFySUVMVZlMSQk6Ji5CEXxgV9tOOFtHWU0BFNWAg3KepksRa7gHwPqiaKYUX" +
            "qMHCUbMWRFBFDMBIihyC8OWQAFRY20Fi47rmSrKjm1ZbCYRUZRFVYwrB1J2zCv8Qzr8/p/lZvcBAlZkmknjpVCXJFVkpX5U4TTl9XudcKaqCJBZTZeSa1znGiJ3sChXq" +
            "n5yEpsTaSPtY11yZ2o+jPaEphKioNTHlwCpUVrVg2d/2XNRSiSucUiDDfbUluluMqDaKtSbImI/H5SnWlJ4phxRODVJ90i12QwXkTGAiZW1jHWs5uI3j6GYbDmER69zY" +
            "RKK11ppI9bC/L8c/QE1ZlEWKdBKUROERGUzMai2YRYRFVDkR6zpPh7Q0jkhiNQuqWqL3McxEmCGiFgFQ+s5lFNl9eSS3NkYz2aVQkrSUTqzceu8jQ47Rm+nv8ulibTGL" +
            "CmXWXouofEFbXwGHPGtMhM95XU5KTJQBEm2DjI/jOG63+71Xz4RY07LQdECEWYRh1jzBzXovXk56LIc4bCQLW+sjEHIco9mHEAK+w/IyiASomiSJIhN77RO0griqFa4s" +
            "zoM4MzgTrP2Apdxe/vjjjy9fbqZP9EIbEXsAgQ29MIkaSMxaUxEkL4o1k51HgIQRYyWFFETIxB/Rgeg9Ywooc+VOKyk1QcxmEAaxeDJiKSfFNefySNn1L2sbZEF6e/nz" +
            "v/7rzy9DKWLjsGJg5gjK6untEp7V1FSZQEnp8wpOW5GFb45AcGuqP1RbP2J5vBuQnglKjrXWTE7tCVJtbEJEPB1woVSK63FNT+wkkcWG9IDY7eXPP//849Y4VqR71roW" +
            "Avzt8BIWFdu1VBLS17xcqHsksZCZmTH9vvn5BM2QQcRK6b5WsKkHiNU0pQ76QMxwQV5n0Zg2WKFdO4i13+5f/vhyH5orKNYKEmUqViAz694foma66+dqa815hbB7bqxN" +
            "dgr8SUOfUVlHYaCUkkyJDHeG+j4XwJlFWIH75Ix5ndPBu2YXU9RBf7u93O+jkQelz+msKrLrTRGTXe5aa1Z1QUSseV3XNUPUIzJTKiNbBPOIBIHAHwKQrffe2qpMn5gI" +
            "CXJPkIgqUXrrmYSIzIyYc3qQSIQyEwmLWS9A7BhdEkXBc9qoCarCVmYCi/Xem3BGtfpr0tXBdXfknNd1LUrtnp+QVJhFrPUxjulMYsoZFamebXAjitaqv+nuvnxV6unq" +
            "AmZmaVXwjjFGk0rKM9wBAAxQ1RPKBCJtfQxTOJBrznmd15wrs/ma81LJ6/F4PBanroiPqRMMZtXWx23OVEhTTk5fUY+YiqgSVM1aIlMQc/pakRASZ04WFmXrx+12jNZN" +
            "ufrHIAKlIAv4ZtVKfdnaGMMkyanO++uaayXWnNfZSOJ6e3s7Xaj/VIz/8CAK1PoYxyKDmEoqhaeLmWlRNoRF1BKZLoR0j6w+THAyi4qB68/r4dp0UkBVhJOeSNJG/aw1" +
            "43BG+ppzzrXcE3o93jqHxHx7nDO05c/9re87AWBRa+Pm1CBNJRtn0qLeikwq+awSLNpSYREhkFQ1R8RC0jwiI4SSKX0liQ2qVIYyAMpgSmZiq+OqgInwtZavtVY6iZnk" +
            "1PTz8ijSEfPn3S1msT5cFkkzzsZImui9NTMV5opvLNqiuUetgIopc2YSg9QKXH1yd6GdLAozCCZkwFWYWZNbNXczIjwiwsN9OQeYsW7GuTzrXFT9JMvbXHHWNrix1qQz" +
            "ybLthm+V4EU/thYRIM0qzygpIpFgEaZYu4BAkg7LJGYgFiMjUGHFBllbSuTuyyMi6qReUI+MNbsRIKy99+K4fka8QoJYm6RqM06jSGhYLzCS9q8YAKtZC5KoB4szVsAz" +
            "kogQsxfKycxktpmB7oxwuAeSWZtDVDjIr0oGIsLXvK7k5hHut27CKjp6a6b8Ye6BJ5YBVmFtTSUUkRCXpoyIIHdfaxOGRUS1TmZTykWLclEk9qSFi3dcXAIg1oWopkAE" +
            "WLuDhWDk5+O85vJa6PMMWPEdQU2ru6DyWe5RwL57Qlhab8apiAQvmCB8MflavpbjCfuz8K5mQ9M4PT0AxGqtunJclDxmxOJ05TooVrL0IGZE0ZDPa861fM3rPNez1WfW" +
            "uMD13+QelQBEgqX13oyT0yPoCqZcU8Cx3Ku3h40vsVgbXTnIT6FNuEG0pqrWBKxtdBMKhz+pZuEzIEGiQqG0rvNxzeWrkLVzpiRp62P1fZ4WBICP8mnsHKBirZpxog0P" +
            "kCeFT0rOgkMBwNdaK0hYrHXlyKkMBKGQpDRtbFSoszEoCVu5wIz0ZGJVgQvWdZ5zeaw151xzzhTSfl3XsYyRIF3ugfw499jsAVRWs7k0fQXYKZcgFZ5gSU64r7k8qyHW" +
            "jN1Nq9cb4XWmgVit9daaUCB8LQ+waoG5YDYTBOe8HtdambHmco+ITFlzznmZpKo5dCz/IY7/WtgyFzE+ORMsO2w7A0oJFgUh09daDmNtvQ9lXvWwgL7FPLHW2k6JfF7X" +
            "tSJJrZkJJ1zWVHKK63pcK4CMzeulUkRcp6CJak8dy/MTAJIr2hFEmDISGSuSRDQBRNXRoiBCILwgoz7GGArYDpCF7W+Nxq7+0uf5OM9rOlhaNFOmhPhSivTzcV6eRJSV" +
            "/yaDEet6KMJEbKBdRdT+BctjlKYkScAiiECmx6qyQ5KQGcIkSlS5D0ikjeN2P4YktNreom33UUSLh8LImOfb29tjeoIV1kwYyeEenjHfHo8ZYBFibZEknszweUq6qlrI" +
            "mO8cvo/yaUuSSCLKyHDfgQrMsg92ZoGIVOZHbdzuL/ejU4SpiGojHWP0vgUxBdHkOt/e3t4uL+RpN8SK+5s+r+uawcpcuITN5ckUUziXqTZqc+0i4MN8WtFEIxMRvuYs" +
            "ni52H3mfl++0CmXpx+3lfj8apquqWuOCXprwTj2WJPx6vL29nSu40sR6ZJ48c/e1HMLaTBCrX9ecDoJPTje1tOURP+J5P+TTYixaEP86z3OuSOzSTlTqeKguDrMCMo7b" +
            "7XYblrRM1ayJtGOMrgxCLLuUQnKdj9fHYwaJCb6jbnBxS7LoKmN0pfBxXec1PQguyDBIHbOfFrZCRJzJDEqf1+OaDrCqmrIWkw3pHp5gMSJtrbXeu2aqqlmLqLCkOyAg" +
            "ZuOY19vjMR3SOOnZshFWVVUIMytJP263bpRr9WaqcyWlE4HkAxXaz/l0VUgb85jTAQkDCxenCPDN7VbQswsoyqpqrfdMbs1UGYiAzHk2o5jX45wrpbGiWBZFF2qta2Zr" +
            "DWnjdrt1Ja8WODN5Ugaz5Hs36p9zTVHHNUjMlIIy1vIkECtBqEg3G7Dug4JMhUEZvgJsZkKxrmt6sEF7SaFExEj7GEOTvIeQjdt9NEmGmrXITIqkDElUV+TzhGnrHPLZ" +
            "zAlIgFj2AW6gStLATAKJLFgsCihqwynAgsBWwYFEmCoWsgyt0JQiasbW+xiaFEFGbRyjKyVQBVqrJy8TYNZNSfgYgNy5R0SAtSWkeSRQCIsUzJzhAdFNPeairnDhAU4r" +
            "QQkuJv3yJHoCgmLSQaKKVFU1qe45OJMaaRFg1ryuVQTJ0EpySMzaz1TZH/Np38E/SDtrX1EwyWYcg6WqEaRUPyLJWjORBKv1IPHIAGVWzlY9qUQSK3mCWZRSVVW5GiDg" +
            "BC+wMTz9Oq+5AgEWTQHRsxO2qVIf5tPha7pnEki69V20RbIyMwu+vRER61pYqa03UdSsiRkZu13vvmVSIGKh2OAJiYqIkIioGXEmOAiR8PW4rhlPUkwSxKyP47iN3lTk" +
            "k3y6QkrBSWKECJ/rci/eYuFmqgmmjHU9Jgw2RhMjT7A2ABkIz8xIZJYeg3hzY4usuKW6TESsTM09MtMRaz7O6WCREkwKtB/H/f5yvx3d9ANQvfLpWPOaATZjM6GMRfAo" +
            "sXF1Xc2CkoCY13mlkY3RpVF4kqhVtxdb9Ssi+d5L52e3sma/d2wJn3JFhq/rulaSMqsg1ZN03L/88ceXl/vnHKadck5PbskCZqZgpHsyqZcWsXWSQESs63GFUTuOzs7w" +
            "SBIjQnpEPknnYEVR7ITrQXcq2UWpnXLTAeby8LWulVC10ZQQkazj5c8///zzy/1oH670d0rKFVzUliK3XucMKf4osbVB4kmeseYVLuNxNHahzJLbIKsoZWYxSFTLn0rY" +
            "7GuCiiK2dQ+8lR5zRfV9tY1jDGPKAOt4+eO//scfX+5Ht0/29LMvHg4FEUFoXefb69sVMoKYOxNrI5FIpsxYbvM8H427bnLtE00Gkpilie1shaRWVJx8elIpE90YG+T1" +
            "iEiIFM2oCSHAOu5f/sefX+7HaKofnB47ny46XrFuXbHO17/+ej1Dx0pCGkgMJJ5LZVcY19kFtjuzwkyZyAgga0/UNk8ipE+FUsy1WSoZQVT94Fm6L2nj/vLycitiCEm/" +
            "vfzx5eU2ftwdv+DT3ldh2uGNaT2+/vdfX8+QMSMzmwCsxJSt91aE3TUvZTKupI1gLbwYKFGs58RTnhyTQwi+ErzV8gR3X+7uIBJp4/bljz++3LtxZoBt3O/H6MV/5E/y" +
            "aWvhnhmRvkwJ6/z6139/PV3G8kw/TAgkxBjruM2gFApfHmBRVebcLQdUi4mLFVDNCMpYWMzFrSFQhivg7uHVZFHrx/3Ln3/++TKMMzyh7TjGDoj8WT6tLaMAQQczsM63" +
            "r29vD+drBcJnN2Vh4U4Z4RAnqUaVWnsyAjMzYq25kiSVKTcJgCk9n/Tm0tYvNizftRQXb+7l5Y8/Xw6TdPdg681MfoOaMosCiQxfFCuRWPNxXnMt8iTKWKO3omUwgGSd" +
            "UFNRba23zcBaa60515wrSbQYkkkiKtVNJZZ6NzkTAl61FLGa9dHHOI7b/SiCS0Ca6S9I7/fbAyzvfDHJtQq83LgaCyP9GKP1LlzPhbbLuY3Rxzi6MiKr9r8KIE+SqAnS" +
            "Nx0liEl2izUzlPxaW4fbeu+9eHO9a7qKg22XeZ+uNJHsk/o6Oeb0Z4KWSblOonCPARZVKTLJ6WTjdr8dR5PKgNe85nXNtfG1WiMRsdZMEpTMUkT9pPCpFKUnKTF9yTdE" +
            "1JSESYPN5Pd6RCYSQhaQR+kzEqV2DQAxZa+5blRQtDu032+32zBO1NnrcxV7B6Vq5bJtaN2kdgozRVJ6zT99rWTeAJqJcHHxiJlc6akc+03lUkwDKxSoyhIxZvVMZoQv" +
            "YRYxNYjhILJF2m73o1cr690ShUpkuqFVItHWepfK8ZlyJSN2jClto7XerLZQBVQRJk1SK6CX+HPPGlQSq9aamYMAYRa1SCrcyZepioqxWEsxaD96sy1TeYIXapkAoiRV" +
            "5Z8wutXXAFwomHLFOzfz2aPYIZWIlYQzSaw2yKdQ7zusTmJtrIB6bqH3O50CsaoDIiARJdJmykhCJIhVW2seW2qSiaxET9T60UoIlbEEviZiVWFQlTE/j3RseEG1PvFX" +
            "hd/PCn13XyvA1m8ka3lEsLDuZr8IZSxVEVLKAFCtN2LKjCTRltvvAshgTkp5xq1xWPW4/KJcKpttKlZ7ARkS21mgBIPYNfNvVzozfK015wzSAdZ5zZnFtXk+DoXlq3JS" +
            "+vIgBSOU91bUhu3RgQyX2KDhlkiMpky5JvkyK5ObavQyIZCRbFnMfhHRikHC/Hkn4OlZc805fQXboVYQfkLEetMt89icAUKsuZzEvbVnH4dFW5JwWdpoSKEWm/Q0xjAm" +
            "Fyx9f7JYRJSpiHPJbWuU9uGMd+eA7x2NPvCsKRyZVFQFfiGdmK2PJlx5+9MKKHzOldyaNdugDbFof3LayquG6sFUs9b6MAYHlwYUxEJUJPhAgjXFc9v4AMlbagaGfOrD" +
            "tD1rHtcCq5kopwmlBwlJdVbCM1lb780qCs3AsmbNTFVJuED3ylo3baOIh621aj8HZaw55/QEW4qYKmd6JBv3JN7GTIFvh99nPkzbs+bx+tfr6WQDYlzgcjgbqbWhnG4J" +
            "0dZbE3gwIzwjtsYKm7T1pBLVOVRkg0papOTD5+Pt9XFOh7CpiFLmWg6h4pkpI52CKRGVjJl94sNUnjXn69evj+AOLuguwz2k3tk4LUDFI+aciKkaTMin7qBqVRXmykDB" +
            "4mTtOG6329FNGJHXWdqBx3SSkp8V5FB1c2vvc8bualrrvXYJf+hZM6/H69dHaIr1kNzyoacWTRLJbL13U3ZjBEicSjVtJkwJMDObqWy1jZP128vLy/3opeE7375+/euv" +
            "r29XoLyRKBcjQWxtHLejm1Cs4NpgAbY+svwj+GPPmmL2p5INV0SURn5rRlQqIPfRTdibMIleAVHrvasykgtLZfQmTMyqjjZuX758uR/DhMLn4+3t9fXtcS4SaaM1RiBE" +
            "hHXc719ebkcTBHYdMR3SjiDRzwpbZLrPeV5hutZSed+VVt0fIWKzMUZvTGHCbG05eOu6KHZEbkrZN6l4kR33L6UWIfi6Ho/HOacnRPtx9MaxkAlhu335848v99EEkUD4" +
            "uq65oN1JWkvg2/74KSK6zzUjbc6uEhs3sv5O59tBwoRChbX3UlDYXucUUmvdFGEqrK0vstvty5f76Iz0qLQ1iaVJP2630ciNiTWl37/8+cfLfRhTItPX9TjPBTtIu8dn" +
            "OpfiXviakdp7Y81I1ibcjtFbkaU2CcmEg4nUupf3jzDSkbkTOqM0U7E2nOy4v7zcuiIWvApK7Smk43a/DYNPZnXoKFpcU9mFyPl4eyzYr23EnxRF29UouDUjIwdrh/Tb" +
            "bXQTSjCKDblp7Cz2zBYoA7HxPmtN0Ey0jdPJjuN+H41jOtIjIW2QOVm/3Y+uWMpiQTruBcpw2Rumr+t8TDIZ68fe1gcE2QyfQqaCJpFsQtZv91s3pkwIa8S7qaAoJHfr" +
            "g6po3fisclO1NqaT9eM4umLlpMyENEj3JOvHMZqkMrEmWT96E0KGyF4GX3MCtcz4Xj9rv9ioZDigwuhKSarSRy1JpCdDWnNX4ox3sdL2Rywcf8slFarajhVUxw0nebmi" +
            "aNP+NCtrSo7M5KyWXC7OVFXmCkS+SH5tbv2kRyzRQSSpCIUJgTdQZRyRHpRs1oSNM9badRUVjBgREUTiXoWqtu6eJNbMFJGqpcWV7VCoTYWSvNwKVSj9Sm+tNzKhJ9IV" +
            "+QtZ7Ifu1rsIHDFVGW17to0xhoGYciFIRQnG5D6Xl0/ZTkUjwpGs7gFW1tY2eqAiyV4Hp7z7IEiZLgHll4J0xLTW/SjNJpddFeh3Bg77GWqtobjGMNUyQGtNM4UyMkiY" +
            "M5qQ+1xrt0dFhN7jp65t7whDPss9Tt3kfuKtgmBOnys3vpREKVPURpBUJvA0jXovEH85pyuAtT5upzOEKWOzNriQFmYmeJ2hsyvvTvRWummpqBN77qCnNQZ9y5zLnYbF" +
            "+jG6MvxyhC93sACRBBJzSDn6vVM5+PPCllnE+nG7XalJVjntTt3w/MIID1/X0U0qw1wBVu2tlSWW5LOLnyhRVAJJoCy0bi+htjEa4QpKL1euUp1mkiZbM9USg/H2mPmh" +
            "I/fj9lBr47gtmKNS8Ur0wpcAiYLj1ryuczRFrjmv5STaASIhFiWhrRXOFGLs+Wfhuk/+C7FYbxwhlOGRKcWFDASStPemVrtmW4R+Ihnh7VnUxjFDFujJFkDGmkLORV+I" +
            "lazn6N04fc5rBmkDi2UJ3okqC3cVLjqqe0S1K7av3rPbwhAqnzlRayaEVZSNNadGXNOf2irb5jX8YSdAVK31VS58spk5UyhdBe7lzga2a3QV+JzXCtFki0xh1jKVY/gS" +
            "JuiGydxLB5DlpolKH1Oenrgs1nsTSimzNV+T1c/znFulZCoiv5Ngs4joTusr82PEMhNGeFTTW211Zfhcl0Ma2y5etvyYYjERQTn8Oh/n5UFFaFuJ6kGuNVmfHW3TNnoX" +
            "SmFiZ4RfYH97O6/FTcpB5DNTkh3Wctu91omTAcSyXX56ZLjDPVwYPtcMGNlTPvfUaW1FpVHOx9vr22MGWK0JpZcmeM1LWbGtb8X66F0pmEEEhs/Een19PcNyC6M/RE1B" +
            "+EYJjgQTgwlE9cM//eOiOFigZMq11kpGqduQ5b8CRIDVE+gc83q8/vV2Oth6N0V6ZrKvSwVGq3JEstZaUxIgMpPSyfN6/fp2ZW+bof17sGbO6Z60zxgQOLkI5iaS5WlJ" +
            "KHbmcocq6jBMZIow0j1Ie4KgseZ1Pr6eK7n10VVQ7mOLhcLYp8emAaoqlbQUlA7yx+vr2yS07Xj8ST6N3WaeawVkM0OYCHXYipopgViKjZBR1I/6GYSR1Y7J8BUkI8Fk" +
            "paqY50r2zGjFEKkcK4xjTo+fgjQoiTLX4+3xWEStSPeR+ZFuHLXSvpZHMokI490cITM5AQixgusXy9iQo9WZVKkNMtYK2ExWAefzA0hcq0OTIEpnhEpuKDt2dy4KxAEQ" +
            "c17XXMT9us7zqnThdwTZZ+h6Z4YpJVESSworEfNmKNE7qjG6cWCtzdwOsoPEVJTErPdFQiUTKP9LQjqlMHx6RDAvZS5Bv0fS9qeOCHqqX9xbfujV+zQQZS4rzPIXfYIk" +
            "yUUCenpxbF9OG7fNGAP8utZydw9ui7U3Y7F+86AWKBsLPEsGRAphTw3MgFDOufk42Cyj3V+d8/OV/q2v6cYROcv6KUFl/nm73V/u9y6RFNfjvJa7pzQvYyhpSEibnvzc" +
            "cKV6qrAYm39EhBTKNeeKepaYRY3IhDdY9ZnY/Xe+phSkIqrMqHZs5Dtv8/5y67QWp59vj3Lw7mjHfQZURNT6XLG9lZjKYDi2CqLyMmdKofTlxQtgiFonoza6btXsv/A1" +
            "BVXrnDnLPyQAElbrx3G7HY3ISrl+Lg/S4HFd1Qc0tT6XZ2XDBQPHbnkGkEkOpBMTttzKBGRtkIJt3I9fbfP+qa+pUPF4eBuheBCxyoaXessQIaSvNT1Iqe1nythU7Zor" +
            "EwWv6dNSPRPpKrwkq01HAgOZqqQyoEHSxu2lanT+V76m9KTHpfu63InEeJdTKtszFRkRXh3HyKKHVQrt+fTFUIon7yDdrzbX0xgj8915mIgTYn0c95eX4xNLo7/zNZXn" +
            "99m49HJAGnRt3f42TBBhBqiOoeL3EaWXipwLJVbKajYiM3zMNX0H1WprMZMLi0KsjXE77rf7duL7X/U1ffpBb583XzMhDu3ndc2pRJUTNbPQfDoZPx94IMKDwCTaujHC" +
            "m3sCGbHmeopHy4KEGKHCYmx9HOM4xnE7un7mw/Q3vqaFMci+JSA8yUhsvI0u6OrJ2vqYnol4tkEBZKn4rxWkZEVE47RWklhsEuV3dzYA4bNYs+O4jaOPPv7BSn/qbcoi" +
            "Qq31UtSXsKg3U8qjIVnbGOtpmvFkURDWvM7zXMnK2zh5k0A3UFs1fdI7gOKTiJNbvx230XsN+Ls9/ZmvKXPZ/KtWLbo8GBlT910M4ylOqbxWdn3innmdj/M8HdxEm7sF" +
            "ax1RRcby70AfJqJwQZKQtOO4Hb16Nb89PT71NS0ygTUj5acaxsHp16OaUY2wHdE8qSAId/clcZ3n47yCRTLdlzJSNiGZkbbes+ASwTClBQnbtpZ6p+r/C19T3hpcbgJU" +
            "J3olCfllpioCwb6oo4OTih63pnCcj8d5TVQf2RenqWy8TigZWaG6IGMhFhHjlNaP3lR4c+P/na9pWbkNDeGC+GYZe8Sa19kba7niiFpSQreck2Wd53nNKOI00uGiYtZI" +
            "wEIkjPR1rVUGISoJFiGV3lu1lgBiTfwrX1Opc9agDLCNOefyFRBEhLsk1lqRJGqUqC76nMzruuZyqFhrKqjrGVofLLo5hrGu81pJor2ZcnVFy6oTQZxJkpnyb3xNaVvk" +
            "QAgk/bZWmWCzyUZn5nltq2OATSjDJ5PPtTxLcG28c8c+gtVStvHTdT1WUXiA3cjfF0kQWMAaBvqo5/I3vqbb4KU1EgLb9PA1H+djpnRThpNfj+tcAVYBsdY1LczLvYjK" +
            "fXSF+5wO6U7We4K3fe/yFSRgja0YZYoIBhMY/DPa+7/ia1oCZFZm7Z4Zfj3exiPIRlPKmOd5Ti+Uh1lbrTVFJATW+nE0joh5XuABHd+HlB9/bBRrSOhZNvxGffHuhQAO" +
            "IqbiEOw7KLaBqJmoiHmC0q+33vqC9O/wyO0qKNq6CSE4wULcxhjDiAg+rxCyp4C9TIxaQElKupFIBJhY+Zuc53MAUsqvRWOXMOXTuZvHatbqJhfVliCCX82srWQrZXtV" +
            "F6JNtwdUMeFEG6SPMbplCiEi6FmIlD/0SJKexNWPjGRkMosZscrT1/QjjW39pK0fM+jnK0qIhHQcY/TWzCgsASZEV1VbQaoMDlU1SjHrVZ+3cj1iZoP24xhdM7VMtvU9" +
            "Zqj1hFj3MrZQTs/t31YLULaUn6mZS+k+nfTjy2Du9+PorRknsrijKiLmARYKJSRpgM36dks2tScDUttxG50zWusLqcd2UN7eoDZWlH8ikQsi6lgvCzJV+4nHZD8K3W8r" +
            "yMb68Nqd+8vLMYoZtgmvIqytQMWcItrXTlB7a626Klw20GrjGMaRfgQs9Xh6lTIZsZY/QdUfS5BEQdZ7Xdajm7v4qztQodPDg7R/dsHR/eU2WjN+Zy0ws1q5LUfv46iL" +
            "dIpbYarCZT8FFClTSSlBuqDjqNpPSIuX++ySIVSYRZ+c9b4t8vnj1JRZWyZJOz6/SuoH/9kUZtZCCDN9zemxa6r2RO/rDrbtEiwUTKyjrq66lTgByqLYyhJCpopIW0Ha" +
            "2hi9PS/7+kRRJNBObOM3l3aN9p3gZJt/5gbJ9hVlJeRSVXlXXhI26kqqomUs1tropszM0B0Ed3teRG357gHsJI8+8Z9mIjFmaeN316PpN25+XTyktltJGbltOEq8uG+2" +
            "q87zvo6NEGrP69ie/sz87bpDEJAsaj32Ifuu2uKP70f82wvwSvgl3+31fF5ut53fn76e3y7L+/5A3TfLYVMCfuxZ4XsmxLvh9a4M+fNLHf/uqsFt7kP8o8583+T47uHz" +
            "w7WE9JsrBj/gGL/P4Xd/8+NNlH9zqWMN/N1n4P3CRvx0d+N3F0D+5jLHj+aDny6G5P/t6zP5k/szP74jk393bSb/9g/w+7/5KIv6txeV4v/Wpab8nwul/zPp/0z6P5P+" +
            "/2fS/xPNzsfLqgUVEAAAAABJRU5ErkJggg==";
    }
}
