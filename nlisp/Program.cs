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
            char c = s[i];

            // 1) 空白はスキップ
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            // 2) '(' または ')' は単独トークン
            else if (c == '(' || c == ')')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            // 3) シングルクォートは独立したトークン "'"
            else if (c == '\'')
            {
                tokens.Add("'");
                i++;
                continue;
            }
            else
            {
                // 4) それ以外はシンボルまたは数値 (区切り文字まで読む)
                int j = i;
                while (j < s.Length
                       && !char.IsWhiteSpace(s[j])
                       && s[j] != '('
                       && s[j] != ')'
                       && s[j] != '\'')  // シングルクォートも区切りとする
                {
                    j++;
                }
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

        // --- 新規追加: シングルクォート（'）が出現したら quote 式に展開 ---
        if (tok == "'")
        {
            // ' <expr> は (quote <expr>) と同じ
            pos++;  // ' を消費
            // 次の S 式をパース
            var quotedExpr = Parse(tokens, ref pos);
            // (quote quotedExpr) を LispCons でつくる
            // (quote quotedExpr) → new LispCons(new LispSymbol("quote"), new LispCons(quotedExpr, LispNil))
            return new LispCons(
                new LispSymbol("quote"),
                new LispCons(quotedExpr, LispNil.Instance)
            );
        }

        // 既存の処理: "(" ならリストをパース、")" はエラー、その他はシンボル／数値
        if (tok == "(")
        {
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
    /// <summary>
    /// グローバル環境を初期化し、組み込み関数およびシンボル "nil"/"NIL" を登録して返す。
    /// </summary>
    public static Env CreateGlobalEnv()
    {
        var env = new Env();

        // --------------------------------------------------
        // "nil" および "NIL" を空リストオブジェクト (LispNil.Instance) に紐づける
        // --------------------------------------------------
        env.Define("nil", LispNil.Instance);
        env.Define("NIL", LispNil.Instance);

        // 算術演算子
        env.Define("+",    new LispFunction(BuiltinAdd));
        env.Define("-",    new LispFunction(BuiltinSubtract));
        env.Define("*",    new LispFunction(BuiltinMultiply));
        env.Define("/",    new LispFunction(BuiltinDivide));

        // 数値比較演算子
        env.Define("=",    new LispFunction(BuiltinNumEq));
        env.Define("<",    new LispFunction(BuiltinLess));
        env.Define(">",    new LispFunction(BuiltinGreater));
        env.Define("<=",   new LispFunction(BuiltinLessEqual));
        env.Define(">=",   new LispFunction(BuiltinGreaterEqual));

        // リスト操作
        env.Define("cons",   new LispFunction(BuiltinCons));
        env.Define("car",    new LispFunction(BuiltinCar));
        env.Define("cdr",    new LispFunction(BuiltinCdr));
        env.Define("append", new LispFunction(BuiltinAppend));

        // 新規追加: list, length
        env.Define("list",   new LispFunction(BuiltinList));
        env.Define("length", new LispFunction(BuiltinLength));

        // シンボル/NIL 等価判定・アトムチェック
        env.Define("eq",     new LispFunction(BuiltinEq));
        env.Define("atom?",  new LispFunction(BuiltinAtomP));

        // defun, function, quote, if, lambda, begin などは Eval 内で特殊処理するため、
        // ここでバインドする必要はない。

        return env;
    }

    /// <summary>
    /// S式を評価して結果を返す。特殊フォームと関数適用を処理するメインメソッド。
    /// </summary>
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

        // 3) リスト (LispCons) の場合
        if (expr is LispCons cons)
        {
            var head = cons.Car;

            // --- 3-1) quote ---
            if (head is LispSymbol symHead && symHead.Name == "quote")
            {
                // (quote <datum>) → <datum> をそのまま返す
                return ((LispCons)cons.Cdr).Car;
            }

            // --- 3-2) begin: 複数式を順に評価し、最後の結果を返す ---
            if (head is LispSymbol sBegin && sBegin.Name == "begin")
            {
                var seq    = new ConsList(cons.Cdr);
                LispObj result = LispNil.Instance;
                while (!seq.IsEmpty)
                {
                    result = Eval(seq.First, env);
                    seq = seq.Rest;
                }
                return result;
            }

            // --- 3-3) if: (if <test> <conseq> <alt>) ---
            if (head is LispSymbol sIf && sIf.Name == "if")
            {
                var args       = new ConsList(cons.Cdr);
                var testExpr   = args.First;
                var conseqExpr = args.Rest.First;
                var altExpr    = args.Rest.Rest.First;

                var testResult = Eval(testExpr, env);
                // NIL 以外を真とみなす
                if (!(testResult is LispNil))
                    return Eval(conseqExpr, env);
                else
                    return Eval(altExpr, env);
            }

            // --- 3-4) define: (define <symbol> <expr>) ---
            if (head is LispSymbol sDef && sDef.Name == "define")
            {
                var args      = new ConsList(cons.Cdr);
                if (!(args.First is LispSymbol varSym))
                    throw new Exception("define: first argument must be a symbol");
                var valueExpr = args.Rest.First;
                var val       = Eval(valueExpr, env);
                env.Define(varSym.Name, val);
                return varSym;
            }

            // --- 3-5) lambda: (lambda (<params>) <body>) ---
            if (head is LispSymbol sLam && sLam.Name == "lambda")
            {
                var args      = new ConsList(cons.Cdr);
                var paramList = args.First;         // LispCons または NIL
                var body      = args.Rest.First;     // 本体式
                return new LispFunction(paramList, body, env);
            }

            // --- 3-6) defun: (defun <name> (<params>) <body>) ---
            if (head is LispSymbol sDefun && sDefun.Name == "defun")
            {
                var args = new ConsList(cons.Cdr);

                // 1) 関数名シンボル
                if (!(args.First is LispSymbol funcNameSym))
                    throw new Exception("defun: first argument must be a symbol");
                string funcName = funcNameSym.Name;

                // 2) パラメータリスト (LispCons または NIL)
                var paramList = args.Rest.First;

                // 3) 本体式 (単一式とする)
                var bodyExpr = args.Rest.Rest.First;

                // 4) ラムダ関数オブジェクトを生成して環境にバインド
                var funcObj = new LispFunction(paramList, bodyExpr, env);
                env.Define(funcName, funcObj);

                // 返り値は関数名シンボル
                return funcNameSym;
            }

            // --- 3-7) function: (function <symbol>) または (function (lambda ...)) ---
            if (head is LispSymbol sFunc && sFunc.Name == "function")
            {
                var argsList = new ConsList(cons.Cdr);
                if (argsList.IsEmpty || !argsList.Rest.IsEmpty)
                    throw new Exception("function requires exactly one argument");

                var targetExpr = argsList.First;
                // targetExpr を評価して関数オブジェクトを得る
                var funcObj = Eval(targetExpr, env);
                if (funcObj is LispFunction lf)
                {
                    return lf;
                }
                else
                {
                    throw new Exception("function: argument must evaluate to a function object");
                }
            }

            // --- 3-8) 通常の関数適用 ---
            var evaluatedHead = Eval(head, env);
            if (evaluatedHead is LispFunction func)
            {
                // 引数リストを評価
                var rawArgs        = (LispCons)cons.Cdr;
                var evaledArgsList = MapEvalArgs(rawArgs, env);

                if (!func.IsLambda)
                {
                    // 組み込み関数
                    return func.Builtin(evaledArgsList, env);
                }
                else
                {
                    // ラムダ関数 (クロージャ) を呼び出す
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

    /// <summary>
    /// 引数リスト (LispCons) の各要素を評価し、新しいリストとして返すヘルパー。
    /// </summary>
    private static ConsList MapEvalArgs(LispCons rawArgs, Env env)
    {
        if (rawArgs is LispNil)
            return new ConsList(LispNil.Instance);

        var firstEval = Eval(rawArgs.Car, env);
        var restEval  = (rawArgs.Cdr is LispCons restCons)
                            ? MapEvalArgs(restCons, env)
                            : new ConsList(LispNil.Instance);

        return new ConsList(new LispCons(firstEval, restEval.Raw));
    }

    /// <summary>
    /// ラムダのパラメータリストと渡された引数を新しい環境に束縛するヘルパー。
    /// </summary>
    private static void BindParams(LispObj paramList, ConsList args, Env env)
    {
        if (paramList is LispNil && args.IsEmpty)
            return;

        if (paramList is LispCons pc && !args.IsEmpty)
        {
            if (!(pc.Car is LispSymbol sym))
                throw new Exception("Lambda parameter must be a symbol");
            env.Define(sym.Name, args.First);
            BindParams(pc.Cdr, args.Rest, env);
        }
        else
        {
            throw new Exception("Parameter/argument count mismatch");
        }
    }

    /// <summary>
    /// 数値を期待し、LispNumber へキャストするヘルパー。
    /// </summary>
    private static double AsNumber(LispObj obj)
    {
        if (obj is LispNumber num) return num.Value;
        throw new Exception($"Expected number, but got {obj.ToRepr()}");
    }

    // --------------------------------------------------
    // 以下、組み込み関数（Builtin）の実装
    // --------------------------------------------------

    /// <summary>
    /// '+' : (+ arg1 arg2 ...)
    /// </summary>
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

    /// <summary>
    /// '-' : (- arg1 arg2 ...)
    /// </summary>
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

    /// <summary>
    /// '*' : (* arg1 arg2 ...)
    /// </summary>
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

    /// <summary>
    /// '/' : (/ arg1 arg2 ...)
    /// </summary>
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

    /// <summary>
    /// '=' : 数値の等価判定 (複数引数可)
    ///    例: (= 2 2 2) → T, (= 2 3) → NIL
    /// </summary>
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

    /// <summary>
    /// '<' : 数値の小なり判定 (連鎖可)
    ///    例: (< 1 2 3) → T, (< 2 2) → NIL
    /// </summary>
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

    /// <summary>
    /// '>' : 数値の大なり判定 (連鎖可)
    /// </summary>
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

    /// <summary>
    /// '<=' : 小なりイコール判定 (連鎖可)
    /// </summary>
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

    /// <summary>
    /// '>=' : 大なりイコール判定 (連鎖可)
    /// </summary>
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

    /// <summary>
    /// cons: (cons a b) → 新しいリスト (a . b)
    /// </summary>
    private static LispObj BuiltinCons(ConsList args, Env env)
    {
        if (args.IsEmpty || args.Rest.IsEmpty || !args.Rest.Rest.IsEmpty)
            throw new Exception("cons requires exactly 2 arguments");
        var a = args.First;
        var b = args.Rest.First;
        return new LispCons(a, b);
    }

    /// <summary>
    /// car: (car lst) → lst の先頭要素。
    ///       引数が空リスト (NIL) の場合は NIL を返す。
    /// </summary>
    private static LispObj BuiltinCar(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("car requires exactly 1 argument");
        var lst = args.First;
        if (lst is LispNil)
        {
            // 空リストなら NIL を返す
            return LispNil.Instance;
        }
        if (lst is LispCons consList)
        {
            return consList.Car;
        }
        throw new Exception("car expects a list");
    }

    /// <summary>
    /// cdr: (cdr lst) → lst の残りのリスト。
    ///       引数が空リスト (NIL) の場合は NIL を返す。
    /// </summary>
    private static LispObj BuiltinCdr(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("cdr requires exactly 1 argument");
        var lst = args.First;
        if (lst is LispNil)
        {
            // 空リストなら NIL を返す
            return LispNil.Instance;
        }
        if (lst is LispCons consList)
        {
            return consList.Cdr;
        }
        throw new Exception("cdr expects a list");
    }

    /// <summary>
    /// append: (append list1 list2 ... listN) → すべてのリストを連結して新しいリストを返す。
    ///   例: (append '(1 2) '(3 4) '(5)) → (1 2 3 4 5)
    ///        (append) → NIL
    ///        (append '(a b)) → (a b) のコピー
    /// </summary>
    private static LispObj BuiltinAppend(ConsList args, Env env)
    {
        // ダミーの先頭ノードを作成し、tail が現在の末尾を指す
        LispCons dummyHead = new LispCons(LispNil.Instance, LispNil.Instance);
        LispCons tail = dummyHead;

        // 引数リストが空なら NIL を返す
        if (args.IsEmpty)
            return LispNil.Instance;

        // 各リスト引数について、要素をコピーして続けていく
        var remainingLists = args;
        while (!remainingLists.IsEmpty)
        {
            var currentList = remainingLists.First;
            // currentList が空リストなら何もしない
            if (currentList is LispNil)
            {
                // スキップ
            }
            else if (currentList is LispCons consList)
            {
                // リストを走査し、要素を1つずつ取り出してコピー
                LispCons iter = consList;
                while (iter is LispCons)
                {
                    // 新しいノードを作って末尾に付加
                    var newNode = new LispCons(iter.Car, LispNil.Instance);
                    tail.Cdr = newNode;
                    tail = newNode;

                    // 次の要素へ
                    if (iter.Cdr is LispCons nextCons)
                    {
                        iter = nextCons;
                    }
                    else
                    {
                        // Cdr が NIL の場合、走査終了
                        break;
                    }
                }
            }
            else
            {
                throw new Exception("append expects list arguments");
            }

            remainingLists = remainingLists.Rest;
        }

        // ダミーヘッドの Cdr が連結後のリストの先頭
        if (dummyHead.Cdr is LispCons resultList)
            return resultList;
        else
            return LispNil.Instance;
    }

    /// <summary>
    /// list: (list arg1 arg2 ... argN) → 新しいリスト (arg1 arg2 ... argN)
    ///   例: (list 1 2 3) → (1 2 3)
    ///        (list) → NIL
    /// </summary>
    private static LispObj BuiltinList(ConsList args, Env env)
    {
        // ダミーの先頭ノードを使い、tail が末尾を指す
        LispCons dummyHead = new LispCons(LispNil.Instance, LispNil.Instance);
        LispCons tail = dummyHead;

        // 引数リストを走査し、要素をコピーしていく
        var remaining = args;
        while (!remaining.IsEmpty)
        {
            var newNode = new LispCons(remaining.First, LispNil.Instance);
            tail.Cdr = newNode;
            tail = newNode;
            remaining = remaining.Rest;
        }

        // ダミーヘッドの Cdr が新しいリスト
        if (dummyHead.Cdr is LispCons resultList)
            return resultList;
        else
            return LispNil.Instance;
    }

    /// <summary>
    /// length: (length lst) → リストの要素数を返す (数値)
    ///   例: (length '(a b c)) → 3
    ///        (length nil) → 0
    /// </summary>
    private static LispObj BuiltinLength(ConsList args, Env env)
    {
        if (args.IsEmpty || !args.Rest.IsEmpty)
            throw new Exception("length requires exactly one argument");
        var lst = args.First;

        int count = 0;
        if (lst is LispNil)
        {
            return new LispNumber(0);
        }
        else if (lst is LispCons consList)
        {
            LispCons iter = consList;
            while (iter is LispCons)
            {
                count++;
                if (iter.Cdr is LispCons nextCons)
                {
                    iter = nextCons;
                }
                else
                {
                    break;
                }
            }
            return new LispNumber(count);
        }
        else
        {
            throw new Exception("length expects a list");
        }
    }

    /// <summary>
    /// eq: (eq a b) → a, b が同じ数値 OR 同じシンボル OR 両方 NIL なら T、それ以外は NIL
    /// </summary>
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

    /// <summary>
    /// atom?: (atom? x) → x が NIL OR LispNumber OR LispSymbol のとき T、それ以外 (つまり LispCons) は NIL
    /// </summary>
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
}


class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Nora Lisp Interpreter (C#)");
        var globalEnv = Evaluator.CreateGlobalEnv();

        while (true)
        {
            Console.Write("> ");
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
