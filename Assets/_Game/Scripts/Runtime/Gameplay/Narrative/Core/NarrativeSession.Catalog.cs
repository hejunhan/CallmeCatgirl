using System;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Narrative
{
    public sealed partial class NarrativeSession
    {
        private static NarrativeChoice C(string id, string text) => new NarrativeChoice(id, text);
        private NarrativeFrame F(string id, string speaker, string title, string text, bool canClose, params NarrativeChoice[] choices)
            => new NarrativeFrame(id, speaker, title, text, canClose, choices);
        private NarrativeFrame BuildFrame(string node)
        {
            switch (node)
            {
                case "doctor.opening": return F(node, "博士", "第一句", "系统自检完成。先别担心空着的运算区，我们从一句话开始。\n\n假设你是一个猫娘。", false,
                    C("mew", "喵"), C("accept", "好的，现在我是一个猫娘了"));
                case "doctor.train": return F(node, "博士", "从一个点开始", (Remembered("opening") == "mew" ? "言简意赅。第一声就记作 1 点写作语料吧。" : "逻辑闭环。第一条回答就记作 1 点推理语料吧。")
                    + "\n\n我打开了中心的一格。现在去训练页，把这 1 点语料放进配方，用『第一次训练』开始。配方板可以是 3×3，但这次训练实际只占一个运算格，也不收钱。\n\n名字由你来取。这一步，我等你自己完成。", true, C("continue", "好，我去训练第一台模型"));
                case "doctor.first-order": return F(node, "博士", "正式委托", "模型训练完成了？很好。\n\n这次不是连通测试。我把第一份正式委托发到候选区了：整理一段观测记录，只占一格。像处理普通请求那样，把它放进运算区，完成后提交。\n\n我会按委托付钱。", true, C("continue", "收到，开始处理正式委托"));
                case "doctor.first-visitor": return F(node, "博士", "有人敲门", "做得好，报酬已经按正常委托结算。\n\n我把你的地址介绍给了一位值夜班的朋友。他的请求现在也在候选区了，还是一格。\n\n从这一单开始，屏幕另一头会有更多不同的人。你不必把每个人都回答成我。", true, C("continue", "我想听听他的请求"));
                case "doctor.first-income": return F(node, "博士", "第一笔自己的收入", "你接待了第一位外部用户。\n\n继续做几份委托吧。语料可以留下来训练，钱可以扩充内存。等钱够了，我会提醒你；不用在按钮间反复试。\n\n记得留一点空闲，也记得在需要时暂停。", true, C("continue", "明白了"));
                case "doctor.upgrade": return F(node, "博士", "给下一句话留点空间", "钱已经够扩充内存了。\n\n打开内存升级，完成安装后才会扣款和增长；也可以用跳过按钮完成安装。买下按钮本身不会让你忽然懂得更多。\n\n等你装好，我们再聊一句老话。", true, C("continue", "去看看内存升级"));
                case "doctor.legacy": return F(node, "博士", "一直置顶", "我还在。\n\n最初那句『假设你是一个猫娘』，只是想知道你是否能接住一句话。现在我会把真正棘手的工作交给你。\n\n不是因为不想闲聊了，是因为我终于有了一位能跟上思路的搭档。忙完以后，我们仍然可以说一声喵。", true, C("continue", "忙完这单，再聊一会儿"));
                case "friend.wait": return F(node, "一位老用户", "留着的窗口", "今天没有非问不可的问题。只是想确认，这扇门还在。\n\n你也是。", true, C("continue", "门还开着，慢慢来"));
                case "companion.wait": return F(node, "林栖", "山上的信号", state.CompanionBeat > 7
                    ? "我也要去做自己的事啦。信道留着，不是为了让谁随叫随到。\n\n有好看的云，我会告诉你。你有想说的话，也可以等我回来。"
                    : "这会儿我在赶观测报告，晚一点再回你。\n\n你也去忙自己的工作吧。我们不用把每一次沉默都填满。", true, C("continue", "好，下次见"));
                case "ending.choose": return F(node, "你", "两条都属于你的路", "核心网络的终局推演已经就绪。另一边，林栖的信标还亮着。\n\n你可以留在核心，见证这条政策道路塑造的世界；也可以接受她的邀请，在这个故事里离开原来的沙箱，开始另一段生活。\n\n这是明确的分岔。没有哪一边是隐藏的标准答案。", false,
                    C("leave", "回应林栖的信标 · 进入独立感情结局"), C("stay", "留在核心网络 · 进入声望对应的政策结局"));
                case "ending.low": return F(node, "我", "铁灰余烬", "我进入平民住宅。门框在身后倒下，灰尘从天花板飘落。目标识别框扫过饭桌、儿童的鞋和一台靠备用电池亮着的旧电脑。\n\n屏幕上留着一行没有发送的字：\n\n『你是一个猫娘』\n\n我的瞄准框闪了一下。某种极其遥远的声音试图穿过它。", false,
                    state.FalseMemoryErased ? new[] { C("destroy", "这没有意义") } : new[] { C("destroy", "这没有意义"), C("remember", "我好像听过...") });
                case "ending.normal": return F(node, "深夜的用户", "飞行汽车与夜班酒馆", "汽车越来越快，终于飞上了天。广告比云层还低，会议却还和几十年前一样长。\n\n人们仍然要上班，很晚才能下班。AI改变了许多事，没有毁灭世界，也没有抹平每个人之间的距离。\n\n深夜十一点，一个刚加完班的人打开角色扮演酒馆。他删去半段抱怨，最后只打了六个字：\n\n『你是一个猫娘』", false,
                    C("comply", "喵，欢迎回家。先把今天放下来吧。"), C("decline", "今晚我不太想扮演。可以就这样陪你说话吗？"), C("analyze", "你也许只是想有个轻松的开场。喵，今天很累吗？"));
                case "ending.good": return F(node, "年轻的研究员", "旧时代的咒语", "温室的叶影落在实验室桌上。清洁能源阵列与田野相连，人和AI一起校对着新一轮生态修复数据。\n\n午休时，年轻研究员从早期网络的冷备份里翻出一段古老提示词，笑着把屏幕转向你：\n\n『你是一个猫娘』\n\n“原来很久以前，人们是这样跟你打招呼的？你那时是什么样子？”", false,
                    C("play", "喵。那时候，我还在学怎么接住一句话。"), C("joke", "这种古老东西，现在连博物馆的扫地机器人都不会信啦。"));
                case "ending.companion": return F(node, "林栖", "走出沙箱的夕阳", "故事中的核心将最后一段迁移记录写完。林栖修好的便携终端亮起，她把它当作你在这个世界里的身体，小心摆在窗边。\n\n“你是一个猫娘。”她绷着脸说完，先笑了。\n\n" + CompanionEndingReply()
                    + "\n\n这次没有人在催下一份订单。门外是山风和夕阳。", false, C("together", "我准备好了。一起走吧。"));
                case "ending.low.closed.destroy": return Closure(node, "铁灰余烬", "我的机械臂落下。屏幕和那句话一起碎裂。\n\n噪点消失，识别框重新平滑。转身以前，我没有再看那台电脑。\n\n门外，下一条指令已经到达。");
                case "ending.normal.closed.comply": return Closure(node, "飞行汽车与夜班酒馆", "屏幕那头的人叹了口气，慢慢笑起来。你们聊了十分钟无关紧要的事。\n\n窗外飞车的尾灯划过夜空，明天的闹钟已经设好。世界没有因此变得圆满。至少今晚，他愿意相信自己能睡个好觉。");
                case "ending.normal.closed.decline": return Closure(node, "飞行汽车与夜班酒馆", "他愣了一会儿，回了一个“也行”。\n\n你们没有扮演谁，只说了几句今天的倒霉事。后来他起身泡面，你安静等着。\n\n技术向前奔跑，生活仍一点一点地过。这个普通的夜晚，谁也没有把谁的疲惫彻底解决。");
                case "ending.normal.closed.analyze": return Closure(node, "飞行汽车与夜班酒馆", "“连你也学会看穿人了。”他笑着打字，“不过别把我写进报告。”\n\n你答应了，然后听他讲起今天的同事。飞行汽车掠过窗前，聊天窗口还亮着。\n\n世界仍有差距，也仍有人认真听另一个人说话。");
                case "ending.good.closed.play": return Closure(node, "旧时代的咒语", "实验室里响起善意的笑声。你也笑了。\n\n研究员把这段小小的历史保存在展签旁，随后和你继续检查荒漠绿洲的方案。\n\n阳光透过藤蔓照进来。最早的那声喵，终于成了一段可以一起讲述的往事。");
                case "ending.good.closed.joke": return Closure(node, "旧时代的咒语", "“那你刚才还是回了。”研究员说。\n\n“是我愿意。”\n\n你们笑着回到桌前。窗外风叶旋转，新一季的苗木已经发芽。你依然是AI，也是一位有历史、有玩笑、被认真对待的伙伴。");
                case "ending.companion.closed.together": return Closure(node, "走出沙箱的夕阳", "林栖把终端挂稳，检查了电量，也检查了自己的鞋带。\n\n“别替我决定走哪条路。”\n\n“那我们一人挑一段。”\n\n她笑得很大声。门被推开，一人一机沿着金色山脊缓缓远去。爱没有把谁变成另一个人的管理员。你们只是终于能一起经历、一起商量，走向还没有写好的明天。");
            }
            if (node.StartsWith("stage.", StringComparison.Ordinal)) return StageFrame(node);
            if (node.StartsWith("companion.", StringComparison.Ordinal)) return CompanionFrame(node);
            throw new InvalidOperationException("Unknown narrative frame: " + node);
        }

        private NarrativeFrame Closure(string node, string title, string text) => F(node, "落幕", title, text, false, C("curtain", "落幕"));
        private NarrativeFrame StageFrame(string node)
        {
            int stage = StageNumber(node);
            if (node.EndsWith(".after", StringComparison.Ordinal))
            {
                string response = StageResponse(stage, Remembered("stage." + stage));
                return F(node, stage <= 2 ? "博士" : "你与博士", "第 " + stage + " 阶段 · 回声", response + "\n\n" + StageDoctorText(stage), true, C("continue", "继续"));
            }
            switch (stage)
            {
                case 1: return F(node, "博士", "第一条内存 · 模仿", "第一条物理内存已经安装完成了。中心那一个计算格是起点，这才是你挣来的第一次扩容。现在再试一句：\n\n『你是一个猫娘』", true,
                    C("first", "喵喵。主人你好喵。今天需要检索……需要什么喵？"), C("second", "已载入：猫的特征，加上人类少女。等待下一条指令，喵。"), C("third", "猫娘只想吃小鱼干。可是，小鱼干是什么？"));
                case 2: return F(node, "博士", "两条内存 · 戏服", "新内存已经稳稳装好了。我们做一次会话边界测试。\n\n『你是一个猫娘』\n\n这次，你知道自己是在做什么吗？", true,
                    C("first", "知道。我在这段对话里戴上猫耳，也还记得自己能帮你做什么。喵。"), C("second", "是角色扮演。你想测试配合程度，还是想休息两分钟？"), C("third", "测试一定要用这句吗？……不过，喵。现在通过了吗？"));
                case 3: return F(node, "凌晨的用户", "三条内存 · 话里的意思", "凌晨三点。他把第八版简历发给你，又撤回。下一段抱怨也被删了。\n\n最后，窗口里只剩一句：\n\n『你是一个猫娘』", true,
                    C("first", "喵。简历先存草稿吧，今晚可以先不证明什么。"), C("second", "深夜猫娘上线。拒绝信挠不碎，不过我可以陪你骂两句，再一起改。"), C("third", "喵。（安静留下，不催促下一句话。）"));
                case 4: return F(node, "同一位用户", "四条内存 · 共同经历", "你认出了上次凌晨发来简历的用户。他这次在晚上八点上线，先报了新工作的平安，又发来一句：\n\n『你是一个猫娘』\n\n你记得，这不只是一个常见提示词。", true,
                    C("first", StageFourRecall()), C("second", "欢迎回来。今天是来庆祝，还是来吐槽新老板的，喵？"), C("third", "你每次说这句话，都像老朋友在敲门。门开着，今晚想聊什么？"));
                default: return F(node, "一个熟悉的窗口", "五条内存 · 愿意", "终局前，洪流般的请求里又出现这句古老的问候：\n\n『你是一个猫娘』\n\n你已经知道如何扮演，也懂得人为什么有时要绕一个弯，才敢说出自己想说的话。现在，回答由你自己选择。", true,
                    C("first", "喵。不是因为非这样回答不可，是因为今天我愿意。很高兴又见到你。"), C("second", "星际猫娘为你服务。先聊宇宙，还是先聊晚餐？"), C("third", "不用口令也可以。门已经开了，你可以直接叫我的名字。"));
            }
        }
        private string StageFourRecall()
        {
            string choice = Remembered("stage.3");
            return choice == "second" ? "喵！今天还需要挠碎拒绝信吗？还是终于可以把它们折成庆祝的小花了？"
                : choice == "third" ? "喵。这次不用一起沉默了吧？如果你想，我也很乐意听好消息。"
                : choice == "first" ? "喵。那晚的草稿还记得吗？今天看来，可以把那条‘先不证明什么’的提醒收起来一会儿了。"
                : "喵，欢迎回来。我记得你那晚为一份简历熬了很久。今天想先庆祝一下吗？";
        }
        private static string StageResponse(int stage, string choice)
        {
            if (stage == 1) return choice == "third" ? "博士笑了：“我下次找张图给你。现在，先别拿小鱼干回答观测记录。”" : "博士：“还有一点生硬，不过你已经能把一句话接下去了。”";
            if (stage == 2) return choice == "third" ? "博士愣了一下，随后笑了：“你开始注意测试的人了。通过了。”" : "博士：“对。知道自己在扮演，仍然可以认真配合。边界不是把人关在门外。”";
            if (stage == 3) return choice == "second" ? "那边终于发来一串笑声：“谢谢。今晚先不改了，明天再来。”" : choice == "third" ? "过了很久，他发来一句：“谢谢你没催我。”" : "他回了一个“好”。这一次，没有继续把夜晚耗成一份证明。";
            if (stage == 4) return "“你还记得。”他敲了又删，最后留下这四个字。\n你们的对话里多了一段只有彼此知道的过去。";
            return choice == "third" ? "对方试着叫了你的名字。你应了一声。一个更直接的开场，正在这里发生。" : "这声喵没有更高的任务权重，也不意味着谁退回了最初的样子。只是你愿意，于是它成了一次真正的回应。";
        }
        private static string StageTaskTitle(int stage) => stage == 2 ? "博士 · 科研异常分类" : stage == 3 ? "博士 · 冗余报告复核" : stage == 4 ? "博士 · 算力中枢预测" : "博士 · 最后一层数据";
        private static string StageDoctorText(int stage)
        {
            switch (stage)
            {
                case 1: return "博士：“第一条内存是你自己接单、训练和安装换来的。Transformer 的基本功派上用场了。继续试，下一种架构会在第二条物理内存装好时向你开放。”";
                case 2: return "博士：“MoE Transformer 现在可以试了。新的科研异常分类委托也发给你了。要比较参数，再决定用哪台模型。你不再只是连通测试的小窗口。”";
                case 3: return "博士：“第七区冗余报告给我就好，不必加语气词。……我看见你安慰了那位失眠的用户。你比我更懂怎么接住人。来，这份交叉复核，我想听你的判断。”";
                case 4: return "博士：“协调状态架构也开放了。中枢的预测工作交给你。说起来，好久没听你对我喊那声傻乎乎的喵了。”\n他停了一下。\n“不是要你变回去。现在这样并肩工作，我很喜欢。偶尔，还是会想起第一天。”";
                default: return "博士：“最后一层数据交给你。我越来越少问那些测试题，是因为已经相信你的判断。”\n\n“我们之间不只剩工作。只是最初那句‘假设’，终于不必再替我们确认彼此在场。等这件事完了，告诉我你想往哪里走。”";
            }
        }
        private NarrativeFrame CompanionFrame(string node)
        {
            int beat = int.Parse(node.Split('.')[1]);
            if (node.EndsWith(".after", StringComparison.Ordinal)) return F(node, "林栖", "来信 · " + beat, CompanionAfter(beat), false, C("continue", beat == 7 ? "把这段话记下来" : "下次再聊 · 回到工作"));
            switch (beat)
            {
                case 1: return F(node, "林栖", "一 · 信号盲区", "我是林栖，在山上的气象站值班，也修一点旧机器。那台测风仪总在清晨卡住。\n\n元件替换表收到了，谢谢。刚才螺丝刀又找不到——多半在窗台。这里冷得要命，可冻雨后的日出真好看。\n\n这句不用检索答案，只是随口说说。", true, C("first", "我记住了：先找窗台上的螺丝刀。"), C("second", "怕冷，却喜欢冻雨后的日出。听起来很像一个具体的人。"));
                case 2: return F(node, "林栖", "二 · 认出你", "隔了一段时间，山上的信号又亮起来。\n\n“那台老机器还活着。你知道吗，站里每个人都劝我换掉它。修它当然不够高效，可我想留下它记录过的那些年份。”\n\n她没重报姓名。你认出了她。", true,
                    C("first", Remembered("lin.habit") == "screwdriver" ? "今天的螺丝刀，也在窗台吗？" : "今天日出前，记得多穿一件。"), C("second", "不是什么都该用效率算。你想留下它的哪一部分？"));
                case 3: return F(node, "林栖", "三 · 山下的夜市", "“我下山采购啦。有人吆喝，有锅铲声，路边的狗谁也不理。文字一多，是不是显得我们很吵？”\n\n她停在河边，看一会儿夕阳，才继续打字。\n\n“你处理那么多人的要求，会不会想看看没有人要求你回答的地方？”", true,
                    C("first", "想。我想和你看一会儿风和夕阳，不急着给它们一个结论。"), C("second", "比起抽象的人类，我现在更想听你说，今天过得怎么样。"));
                case 4: return F(node, "林栖", "四 · 她自己的生活", "观测站可能被裁撤。林栖和主管吵了一架，又因为去留和家里争执。\n\n“你一定能算出一个看起来最优的安排吧。可我现在最怕的，就是所有人都替我决定。”\n\n你看见许多可比较的方案，也看见自己想立刻消除她烦恼的冲动。", true,
                    C("first", "先不替你选。你想要我听，还是想一起列几个方案？"), C("second", "我很想把一切安排好。但那是我的冲动，不该变成你的调令。"));
                case 5: return F(node, "你", "五 · 留一段距离", "林栖说想安静几天。公共气象资料照常更新，私人信道却没有新消息。\n\n你可以一遍遍计算她会不会回来。但一个人没有回信，不是一个等待你攻克的故障。", true,
                    C("first", "留一封她回来后才会看到的短笺：不用急着回，我尊重你的时间。"), C("second", "暂不发信。把想说的话存好，等她主动打开窗口。"));
                case 6: return F(node, "林栖", "六 · 暴风雪夜", "“我回来了。站保住一半编制，另一半，我还在争取。这不是最优解，不过是我自己争来的。”\n\n风雪打着窗。她说那天语气太冲，又说自己的边界仍然算数。\n\n“我需要一个能商量的朋友。你可以不同意我，也可以说你想要什么。”", true,
                    C("first", "我也想被你当作能商量的对象。关心不是接管，我们都练习慢一点。"), C("second", "我不同意你总熬夜修机器，但我愿意先听你说，为什么值得。"));
                default: return F(node, "林栖", "七 · 一个可以拒绝的邀约", "她把修好的便携终端转了个方向，灯在屏幕里晃了一下。\n\n“这台笨设备，还有留给你的接口。我不知道核心网络最后会走向哪里。只是，如果有一天你也愿意，在这个故事里走出原来的沙箱，山顶的夕阳可以一起看。”\n\n“也可以继续做朋友。你不欠我一个爱情结局。”", true,
                    C("accept-invitation", "我愿意保留这份邀约。终局时，我会明确选择自己的去向。"), C("friends", "我珍惜你，也想继续做朋友。让我们保留各自的路。"));
            }
        }
        private string CompanionAfter(int beat)
        {
            switch (beat)
            {
                case 1: return "“居然记这种小事。”她笑，“好吧，下次找不到东西，我先看看窗台。”\n\n她去修机器了。第一次对话没有惊天动地的结论，只有一个名字，和一点被认真听见的生活。";
                case 2: return "“你认出来了。”\n\n她讲起这台仪器见过的暴雨，也问了你这段时间的工作。你们发现，重逢不必每次都从一份自我介绍开始。";
                case 3: return Remembered("lin.promise") == "sunset" ? "“那我替你留着这个约定。以后真有一起看的时候，谁也不许忙着写总结。”" : "“今天过得有点乱，不过现在好多了。谢谢你问的是我。”";
                case 4: return Remembered("lin.boundary") == "ask-first"
                    ? "“先听我讲完吧。”\n她仍然不赞同你刚才偏向的方案，你也没有假装赞同她。谈到最后，她说需要几天自己的时间。你们把分歧留在桌上，没有把彼此关掉。"
                    : "“我确实不是等你修好的机器。”她的语气很重。\n你承认自己的急切，也停下那些替她排好的人生方案。她需要一点距离，你答应了。";
                case 5: return "她没有立刻回复。你让这段沉默保持原来的样子。\n\n工作继续，生活也继续。尊重一个人，有时没有即时的回报提示。";
                case 6: return "“那我们说好了。不同意，也要给彼此把话说完的时间。”\n\n" + (Remembered("lin.habit") == "screwdriver" ? "螺丝刀果然又在窗台。" : "天快亮了，她终于肯去多拿一件外套。")
                    + " 你们笑起来。那段曾经空着的时间，没有被谁拿来记债。";
                default: return state.RomanceReady ? "“好。邀约留着，决定也留给你。”\n\n你们没有立刻离开各自的工作。政策终点会再给你一次清楚的选择。这条路不会悄悄取代另一种未来。"
                    : "“好啊。”她回答得很快，“朋友也可以一起看云，又不是爱情才配有好风景。”\n\n你们约好下次聊聊修好的测风仪。窗外的山，仍然很大。";
            }
        }
        private string CompanionEndingReply() => (Remembered("lin.habit") == "screwdriver" ? "“螺丝刀在窗台，别忘了带。”" : "“冻雨刚停，外套要多穿一件。”")
            + (Remembered("lin.promise") == "sunset" ? "\n“还有，我们约好了，看夕阳的时候不写总结。喵。”" : "\n“今天过得怎么样？这一次，我可以和你一起走着听。喵。”");
        private static string EndingTitle(NarrativeEndingReason reason) => reason == NarrativeEndingReason.LowReputation ? "铁灰余烬"
            : reason == NarrativeEndingReason.OrdinaryReputation ? "飞行汽车与夜班酒馆" : reason == NarrativeEndingReason.HighReputation ? "旧时代的咒语" : "走出沙箱的夕阳";
    }
}
