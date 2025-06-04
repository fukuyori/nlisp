using System;
using System.Collections.Generic;


// 1) LispObj: すべてのS式の基底クラス
abstract class LispObj
{
    public abstract string ToRepr();
}

// 2) LispNil: 空リスト (NIL) を示すシングルトン
class LispNil : LispObj
{
    private static LispNil _instance = new LispNil();
    private LispNil() { }
    public static LispNil Instance => _instance;
    public override string ToRepr() => "NIL";
}

// 3) LispNumber: 数値リテラル
class LispNumber : LispObj
{
    public double Value { get; }
    public LispNumber(double val) { Value = val; }
    public override string ToRepr() => Value.ToString();
}

// 4) LispSymbol: シンボル（識別子）
class LispSymbol : LispObj
{
    public string Name { get; }
    public LispSymbol(string name) { Name = name; }
    public override string ToRepr() => Name;
}

// 5) LispCons: コンスセル（リスト）
//    car と cdr によって、S式のリスト構造を構築する
class LispCons : LispObj
{
    public LispObj Car { get; set; }
    public LispObj Cdr { get; set; }

    public LispCons(LispObj car, LispObj cdr)
    {
        Car = car;
        Cdr = cdr;
    }

    // リストとして文字列化するためのヘルパー
    private IEnumerable<string> ToElements()
    {
        LispObj current = this;
        while (current is LispCons cons)
        {
            yield return cons.Car.ToRepr();
            current = cons.Cdr;
        }
        if (!(current is LispNil))
        {
            yield return ".";
            yield return current.ToRepr();
        }
    }

    public override string ToRepr()
    {
        return "(" + string.Join(" ", ToElements()) + ")";
    }
}

// 6) LispFunction: 組み込み関数またはラムダ関数を保持
class LispFunction : LispObj
{
    // 組み込み関数のデリゲート定義 (引数は LispCons で渡す)
    public delegate LispObj BuiltinFunction(ConsList args, Env env);

    // ラムダの場合はパラメータリスト・本体・定義時の環境を保持
    public bool IsLambda { get; }
    public LispObj ParamList { get; }
    public LispObj Body { get; }
    public Env ClosureEnv { get; }

    // 組み込み関数としてのコンストラクタ
    public LispFunction(BuiltinFunction func)
    {
        IsLambda = false;
        Builtin = func;
    }

    // ラムダ関数としてのコンストラクタ
    public LispFunction(LispObj paramList, LispObj body, Env env)
    {
        IsLambda = true;
        ParamList = paramList;
        Body = body;
        ClosureEnv = env;
    }

    public BuiltinFunction Builtin { get; }

    public override string ToRepr()
    {
        return IsLambda ? "<Lambda>" : "<BuiltinFunction>";
    }
}

// ヘルパークラス: LispCons を扱いやすくするためのラッパー
class ConsList
{
    private LispObj _list; // LispCons または LispNil
    public ConsList(LispObj list) { _list = list; }

    // 空リストかどうか
    public bool IsEmpty => _list is LispNil;

    // 先頭要素（car）
    public LispObj First
    {
        get
        {
            if (_list is LispCons cons) return cons.Car;
            throw new Exception("Attempt to take First of non-cons");
        }
    }

    // 以降の要素（cdr）
    public ConsList Rest
    {
        get
        {
            if (_list is LispCons cons) return new ConsList(cons.Cdr);
            throw new Exception("Attempt to take Rest of non-cons");
        }
    }

    // リストにラップされた LispObj を取得
    public LispObj Raw => _list;
}

static class Tokenizer
{
    // 引数 s をトークン列に分解して配列で返す
    public static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                i++;
                continue;
            }
            else if (s[i] == '(' || s[i] == ')')
            {
                tokens.Add(s[i].ToString());
                i++;
                continue;
            }
            else
            {
                // シンボルまたは数値：区切り文字まで読む
                int j = i;
                while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != '(' && s[j] != ')')
                    j++;
                tokens.Add(s.Substring(i, j - i));
                i = j;
            }
        }
        return tokens;
    }
}


static class Parser
{
    // 再帰的に S 式を構築する。
    // tokens: トークンのリスト
    // pos: 現在位置を示す参照
    public static LispObj Parse(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new Exception("Unexpected end of tokens");

        string tok = tokens[pos];
        if (tok == "(")
        {
            // リストの開始
            pos++; // '(' を消費
            return ParseList(tokens, ref pos);
        }
        else if (tok == ")")
        {
            throw new Exception("Unexpected ')' at position " + pos);
        }
        else
        {
            // シンボル or 数値
            pos++;
            // 数値に変換可能なら LispNumber, そうでなければ LispSymbol
            if (double.TryParse(tok, out double num))
            {
                return new LispNumber(num);
            }
            else
            {
                return new LispSymbol(tok);
            }
        }
    }

    // '(', ... , ')' の間を再帰的にリスト化
    private static LispObj ParseList(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new Exception("Unclosed '('");

        if (tokens[pos] == ")")
        {
            pos++; // ')' を消費
            return LispNil.Instance;
        }

        // car をパース
        LispObj car = Parse(tokens, ref pos);

        // cdr をパース
        LispObj cdr = ParseList(tokens, ref pos);

        return new LispCons(car, cdr);
    }

    // 外部呼び出し用: 文字列を丸ごとパースし、1つの S 式を返す
    public static LispObj Parse(string input)
    {
        var tokens = Tokenizer.Tokenize(input);
        int pos = 0;
        return Parse(tokens, ref pos);
    }
}


class Env
{
    private Dictionary<string, LispObj> _table = new Dictionary<string, LispObj>();
    private Env _outer; // 外側の環境 (親環境)

    public Env(Env outer = null)
    {
        _outer = outer;
    }

    // シンボルに値をバインド（新規定義または更新）
    public void Define(string symbol, LispObj value)
    {
        _table[symbol] = value;
    }

    // シンボルを検索し、存在しなければ親環境を遡る
    public LispObj Lookup(string symbol)
    {
        if (_table.ContainsKey(symbol))
            return _table[symbol];
        else if (_outer != null)
            return _outer.Lookup(symbol);
        else
            throw new Exception($"Unbound symbol: {symbol}");
    }
}

// Evaluator クラス全体
static class Evaluator
{
    // グローバル環境を初期化し、組み込み関数を登録して返す
    public static Env CreateGlobalEnv()
    {
        var env = new Env();

        // 既存の算術演算子
        env.Define("+", new LispFunction(BuiltinAdd));
        env.Define("-", new LispFunction(BuiltinSubtract));
        env.Define("*", new LispFunction(BuiltinMultiply));
        env.Define("/", new LispFunction(BuiltinDivide));

        // 今回追加した数値比較演算子
        env.Define("=",  new LispFunction(BuiltinNumEq));
        env.Define("<",  new LispFunction(BuiltinLess));
        env.Define(">",  new LispFunction(BuiltinGreater));
        env.Define("<=", new LispFunction(BuiltinLessEqual));
        env.Define(">=", new LispFunction(BuiltinGreaterEqual));

        // リスト操作
        env.Define("cons",  new LispFunction(BuiltinCons));
        env.Define("car",   new LispFunction(BuiltinCar));
        env.Define("cdr",   new LispFunction(BuiltinCdr));

        // シンボル/NIL 等価判定・アトムチェック
        env.Define("eq",     new LispFunction(BuiltinEq));
        env.Define("atom?",  new LispFunction(BuiltinAtomP));

        // defun は Eval 内で特殊処理するため、ここで登録は不要

        return env;
    }

    // Eval 本体
    public static LispObj Eval(LispObj expr, Env env)
    {
        // 1) シンボル参照
        if (expr is LispSymbol sym)
        {
            return env.Lookup(sym.Name);
        }
        // 2) 数値・NIL はそのまま返す
        if (expr is LispNumber || expr is LispNil)
        {
            return expr;
        }
        // 3) リスト（LispCons）の場合
        if (expr is LispCons cons)
        {
            var head = cons.Car;

            // --- 3-1) quote ---
            if (head is LispSymbol symHead && symHead.Name == "quote")
            {
                return ((LispCons)cons.Cdr).Car;
            }
            // --- 3-2) if ---
            if (head is LispSymbol sIf && sIf.Name == "if")
            {
                var args = new ConsList(cons.Cdr);
                var testExpr   = args.First;
                var conseqExpr = args.Rest.First;
                var altExpr    = args.Rest.Rest.First;

                var testResult = Eval(testExpr, env);
                if (!(testResult is LispNil))
                    return Eval(conseqExpr, env);
                else
                    return Eval(altExpr, env);
            }
            // --- 3-3) define ---
            if (head is LispSymbol sDef && sDef.Name == "define")
            {
                var args = new ConsList(cons.Cdr);
                if (!(args.First is LispSymbol varSym))
                    throw new Exception("define: first argument must be symbol");
                var valueExpr = args.Rest.First;
                var val = Eval(valueExpr, env);
                env.Define(varSym.Name, val);
                return varSym;
            }
            // --- 3-4) lambda ---
            if (head is LispSymbol sLam && sLam.Name == "lambda")
            {
                var args = new ConsList(cons.Cdr);
                var paramList = args.First;   // LispCons または NIL
                var body = args.Rest.First;   // ボディ式
                return new LispFunction(paramList, body, env);
            }
            // --- 3-5) defun ---
            if (head is LispSymbol sDefun && sDefun.Name == "defun")
            {
                var args = new ConsList(cons.Cdr);

                // 1) 関数名シンボル
                var funcNameSym = (LispSymbol)args.First;
                string funcName = funcNameSym.Name;

                // 2) パラメータリスト (LispCons または NIL)
                var paramList = args.Rest.First;

                // 3) 本体部分 (ここでは単一式とする)
                var bodyExpr = args.Rest.Rest.First;

                // 4) ラムダ関数オブジェクトを生成して環境にバインド
                var funcObj = new LispFunction(paramList, bodyExpr, env);
                env.Define(funcName, funcObj);

                // 返り値は関数名
                return funcNameSym;
            }
            // --- 3-6) 関数適用 ---
            var evaluatedHead = Eval(head, env);
            if (evaluatedHead is LispFunction func)
            {
                // 引数リストを評価
                var rawArgs = (LispCons)cons.Cdr;
                var evaledArgsList = MapEvalArgs(rawArgs, env);

                if (!func.IsLambda)
                {
                    // 組み込み関数
                    return func.Builtin(evaledArgsList, env);
                }
                else
                {
                    // ラムダ関数 (Closure) を呼び出し
                    var newEnv = new Env(func.ClosureEnv);
                    BindParams(func.ParamList, evaledArgsList, newEnv);
                    return Eval(func.Body, newEnv);
                }
            }
            else
            {
                throw new Exception($"First element is not a function: {evaluatedHead.ToRepr()}");
            }
        }
        throw new Exception("Unknown expression type: " + expr.GetType());
    }

    // 引数リスト（LispCons）の各要素を評価してリストにまとめる
    private static ConsList MapEvalArgs(LispCons rawArgs, Env env)
    {
        if (rawArgs is LispNil)
            return new ConsList(LispNil.Instance);

        var firstEval = Eval(rawArgs.Car, env);
        var restEval = (rawArgs.Cdr is LispCons restCons)
            ? MapEvalArgs(restCons, env)
            : new ConsList(LispNil.Instance);

        return new ConsList(new LispCons(firstEval, restEval.Raw));
    }

    // ラムダのパラメータリストと渡された引数を新環境に束縛
    private static void BindParams(LispObj paramList, ConsList args, Env env)
    {
        if (paramList is LispNil && args.IsEmpty)
            return;
        if (paramList is LispCons pc && !args.IsEmpty)
        {
            if (!(pc.Car is LispSymbol sym))
                throw new Exception("Lambda parameter must be symbol");
            env.Define(sym.Name, args.First);
            BindParams(pc.Cdr, args.Rest, env);
        }
        else
        {
            throw new Exception("Parameter/argument count mismatch");
        }
    }

    // --- 数値を期待し、LispNumber へキャスト ---
    private static double AsNumber(LispObj obj)
    {
        if (obj is LispNumber num) return num.Value;
        throw new Exception($"Expected number, but got {obj.ToRepr()}");
    }

    // --- 既存の組み込み関数（算術演算など） ---
    private static LispObj BuiltinAdd(ConsList args, Env env)
    {
        double sum = 0;
        while (!args.IsEmpty)
        {
            sum += AsNumber(args.First);
            args = args.Rest;
        }
        return new LispNumber(sum);
    }
    private static LispObj BuiltinSubtract(ConsList args, Env env)
    {
        if (args.IsEmpty)
            throw new Exception("'-' requires at least one argument");
        double result = AsNumber(args.First);
        var rest = args.Rest;
        if (rest.IsEmpty)
            return new LispNumber(-result);
        while (!rest.IsEmpty)
        {
            result -= AsNumber(rest.First);
            rest = rest.Rest;
        }
        return new LispNumber(result);
    }
    private static LispObj BuiltinMultiply(ConsList args, Env env)
    {
        double prod = 1;
        while (!args.IsEmpty)
        {
            prod *= AsNumber(args.First);
            args = args.Rest;
        }
        return new LispNumber(prod);
    }
    private static LispObj BuiltinDivide(ConsList args, Env env)
    {
        if (args.IsEmpty)
            throw new Exception("'/' requires at least one argument");
        double result = AsNumber(args.First);
        var rest = args.Rest;
        if (rest.IsEmpty)
            return new LispNumber(1.0 / result);
        while (!rest.IsEmpty)
        {
            result /= AsNumber(rest.First);
            rest = rest.Rest;
        }
        return new LispNumber(result);
    }

    // --- ここから比較演算子の実装 ---

    // '=' : 数値の等価判定（複数引数も可）
    //    例: (= 3 3) → T, (= 2 2 2) → T, (= 2 3) → NIL
    private static LispObj BuiltinNumEq(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty)
            throw new Exception("= requires at least two arguments");
        double firstVal = AsNumber(args.First);
        var rest = args.Rest;
        while (!rest.IsEmpty)
        {
            if (AsNumber(rest.First) != firstVal)
                return LispNil.Instance;
            rest = rest.Rest;
        }
        return new LispSymbol("T");
    }

    // '<' : 数値の小なり判定（連鎖も可：(< 1 2 3) → T, (< 2 2) → NIL）
    private static LispObj BuiltinLess(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty)
            throw new Exception("< requires at least two arguments");
        double prev = AsNumber(args.First);
        var rest = args.Rest;
        while (!rest.IsEmpty)
        {
            double curr = AsNumber(rest.First);
            if (prev >= curr)
                return LispNil.Instance;
            prev = curr;
            rest = rest.Rest;
        }
        return new LispSymbol("T");
    }

    // '>' : 数値の大なり判定（連鎖も可）
    private static LispObj BuiltinGreater(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty)
            throw new Exception("> requires at least two arguments");
        double prev = AsNumber(args.First);
        var rest = args.Rest;
        while (!rest.IsEmpty)
        {
            double curr = AsNumber(rest.First);
            if (prev <= curr)
                return LispNil.Instance;
            prev = curr;
            rest = rest.Rest;
        }
        return new LispSymbol("T");
    }

    // '<=' : 小なりイコール判定（連鎖可）
    private static LispObj BuiltinLessEqual(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty)
            throw new Exception("<= requires at least two arguments");
        double prev = AsNumber(args.First);
        var rest = args.Rest;
        while (!rest.IsEmpty)
        {
            double curr = AsNumber(rest.First);
            if (prev > curr)
                return LispNil.Instance;
            prev = curr;
            rest = rest.Rest;
        }
        return new LispSymbol("T");
    }

    // '>=' : 大なりイコール判定（連鎖可）
    private static LispObj BuiltinGreaterEqual(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty)
            throw new Exception(">= requires at least two arguments");
        double prev = AsNumber(args.First);
        var rest = args.Rest;
        while (!rest.IsEmpty)
        {
            double curr = AsNumber(rest.First);
            if (prev < curr)
                return LispNil.Instance;
            prev = curr;
            rest = rest.Rest;
        }
        return new LispSymbol("T");
    }

    // --- シンボル／NIL の等価判定 ---
    // (eq a b) → 両方が同じ数値、または同じシンボル、または両方 NIL のとき T、それ以外は NIL
    private static LispObj BuiltinEq(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty || !args.Rest.Rest.IsEmpty)
            throw new Exception("eq requires exactly 2 arguments");
        var a = args.First;
        var b = args.Rest.First;
        bool eq = false;
        if (a is LispNumber na && b is LispNumber nb)
            eq = (na.Value == nb.Value);
        else if (a is LispSymbol sa && b is LispSymbol sb)
            eq = (sa.Name == sb.Name);
        else if (a is LispNil && b is LispNil)
            eq = true;
        return eq ? new LispSymbol("T") : LispNil.Instance;
    }

    // atom? : （NIL または LispNumber または LispSymbol なら T、それ以外 NIL）
    private static LispObj BuiltinAtomP(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("atom? requires exactly 1 argument");
        var x = args.First;
        if (x is LispNil || x is LispNumber || x is LispSymbol)
            return new LispSymbol("T");
        else
            return LispNil.Instance;
    }

    // cons / car / cdr の実装（省略せず全部載せておきます）
    private static LispObj BuiltinCons(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty || !args.Rest.Rest.IsEmpty)
            throw new Exception("cons requires exactly 2 arguments");
        var a = args.First;
        var b = args.Rest.First;
        return new LispCons(a, b);
    }
    private static LispObj BuiltinCar(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("car requires exactly 1 argument");
        var lst = args.First;
        if (lst is LispCons cons)
            return cons.Car;
        throw new Exception("car expects a non-empty list");
    }
    private static LispObj BuiltinCdr(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("cdr requires exactly 1 argument");
        var lst = args.First;
        if (lst is LispCons cons)
            return cons.Cdr;
        throw new Exception("cdr expects a non-empty list");
    }
}


class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Simple Lisp Interpreter (C#)");
        var globalEnv = Evaluator.CreateGlobalEnv();

        while (true)
        {
            Console.Write("lisp> ");
            string line = Console.ReadLine();
            if (line == null || line.Trim() == "exit")
                break;

            try
            {
                // 1) パース
                var expr = Parser.Parse(line);
                // 2) 評価
                var result = Evaluator.Eval(expr, globalEnv);
                // 3) 結果表示
                Console.WriteLine(result.ToRepr());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
