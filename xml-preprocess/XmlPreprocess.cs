
using System.Xml.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Linq.Expressions;

namespace XmlPreprocess
{
    public class Preprocessor
    {


        public string Process(string filePath, params KeyValuePair<string, string>[] args)
        {
            FileInfo file = new FileInfo(filePath);
            if (file.Exists)
            {
                var xmlContent = File.ReadAllText(file.FullName);
                var keyValues = FindVarient(xmlContent);
                var currentArgs = GetCurrentArgs(keyValues, args);
                xmlContent = FullfillArgs(xmlContent, currentArgs);

                XDocument doc = XDocument.Parse(xmlContent);
                FindAndRemoveUnCondition(doc);

                var imports = FindImports(doc, filePath).ToList();
                if (imports.Any())
                {
                    foreach (var import in imports)
                    {
                        var path = import.GetPath();
                        var importXml = Process(path, args);
                        var targetNode = GetTargetNode(importXml, import);
                        
                        var insertTo = import.RepresentNode.Parent;
                        import.RepresentNode.Remove();
                        foreach (var item in targetNode.Elements())
                        {
                            insertTo.Add(item);
                        }
                    }
                }
                else
                { 
                    // 递归退出
                }
                xmlContent = doc.ToString();
                return xmlContent;
            }
            throw new Exception();
        }

        private KeyValuePair<string, string>[] GetCurrentArgs(IEnumerable<Variable> currentAll,params KeyValuePair<string, string>[] args)
        {

            List<KeyValuePair<string, string>> current = new List<KeyValuePair<string, string>>();
            foreach (var item in currentAll)
            {
                if (args != null && args.Any(a => a.Key == item.Key))
                {
                    var value = args.First(a => a.Key == item.Key).Value;
                    current.Add(new KeyValuePair<string, string>(item.Key, value));
                }
                else
                {
                    current.Add(new KeyValuePair<string, string>(item.Key, String.Empty));
                }
            }
            return current.ToArray(); 
        }

        private XElement GetTargetNode(string importXml, ImportNode import)
        {
            XDocument doc = XDocument.Parse(importXml);
            return GetXElement(doc.Root,import.TargetNode);
        }

        private XElement GetXElement(XElement root, string name)
        {
            // 递归退出条件
            if (root.Name == name)
            { 
                return root;
            }
            //返回IEnumerable接口的对象，都可以实现foreach循环遍历
            foreach (XElement element in root.Elements())
            {
                //递归
                if (element.Elements().Count() > 0)
                {
                    return GetXElement(element, name);
                }
            }
            throw new Exception();
        }

        private string FullfillArgs(string xmlContent, KeyValuePair<string, string>[] args)
        {
            if (args != null)
            {
                foreach (var arg in args)
                {
                    xmlContent = xmlContent.Replace($"$({arg.Key})", arg.Value);
                }
            }
            return xmlContent;
        }

        private IEnumerable<Variable> FindVarient(string content)
        {
            Regex regex = new Regex("\\$\\(\\w+\\)");
            foreach (Match match in regex.Matches(content))
            {
                yield return new Variable(match.Value);
            }

        }

        private void FindAndRemoveUnCondition(XDocument doc)
        {
            var conditions = from XElement x in doc.Root.Descendants()
                where x != null && x.Attribute("Condition") != null
                select new Condition(x.Attribute("Condition").Value,x);

            foreach (var condition in conditions.ToList())
            {
                if (!condition.CalculateExpression())
                {
                    condition.ConditionNode.Remove();
                }
            }
        }

        private IEnumerable<ImportNode> FindImports(XDocument doc, string basePath)
        {
            return from XElement x in doc.Root.Descendants()
                   where x != null && x.Name == "Import"
                   let targetNode = x.Attributes()?.FirstOrDefault()?.Name.ToString()
                   let path = x.Attributes()?.FirstOrDefault()?.Value
                   select new ImportNode(basePath, path, targetNode,x);
        }
    }

    /// <summary>
    /// xml中需要处理的变量
    /// </summary>
    internal class Variable
    {
        /// <summary>
        /// 变量包裹字符串
        /// e.g. $(a)
        /// </summary>
        public string Wrapper { get; set; }
        /// <summary>
        /// 变量名
        /// e.g. a
        /// </summary>
        public string Key { get; set; }
        /// <summary>
        /// 变量值
        /// </summary>
        public string Value { get; set; }

        public Variable(string  wrapper)
        {
            if (wrapper.StartsWith("$(") && wrapper.EndsWith(")"))
            {
                this.Wrapper = wrapper;
                this.Key = wrapper.Substring(2,wrapper.Length - 3);
            }
            else
            {
                throw new ArgumentException(wrapper);
            }
        }
    }

    internal class ImportNode
    {
        /// <summary>
        ///导入者路径
        /// </summary>
        public string BasePath { get; set; }
        /// <summary>
        /// 导入文件路径
        /// </summary>        
        public string ConfigPath { get; set; }
        /// <summary>
        /// 插入节点
        /// </summary>
        public string TargetNode { get; set; }
        /// <summary>
        /// 替代节点
        /// </summary>
        public XElement RepresentNode { get; set; }

        public ImportNode(string basePath, string configPath, string targetNode, XElement representNode)
        {
            BasePath = basePath;
            ConfigPath = configPath;
            TargetNode = targetNode;
            RepresentNode = representNode;
        }

        /// <summary>
        /// 转换目标相对与目标绝对路径
        /// </summary>
        /// <returns></returns>
        public string GetPath()
        {
            var target = ConfigPath.Split(new char[2] { '\\', '/' });
            var current = BasePath.Split(Path.DirectorySeparatorChar).ToList();
            current.RemoveAt(current.Count - 1);
            foreach (var path in target)
            {
                switch (path)
                {
                    case "..":
                        current.RemoveAt(current.Count - 1);
                        break;
                    case ".":
                        break;
                    default:
                        current.Add(path);
                        break;
                }
            }
            return Path.Combine(current.ToArray());
        }
    }

    internal class Condition
    {
        public string ConditionExpression { get; set; }

        public XElement ConditionNode { get; set; }

        public Condition(string conditionExpression, XElement conditionNode)
        {
            this.ConditionExpression = conditionExpression;
            ConditionNode = conditionNode;
        }

        public bool CalculateExpression()
        {
            var expression = ConditionExpression.Trim();
            // 暂不考虑括号优先级情况
            if (expression.Contains(" and "))
            {
                bool result = true;
                var ands = ConditionExpression.Split(" and ");
                foreach (var and in ands)
                {
                    result = result && ConvetEquExpression(and);
                }
                return result;
            }
            else if (expression.Contains(value: " or "))
            {
                bool result = true;
                var ors = ConditionExpression.Split(" or ");
                foreach (var and in ors)
                {
                    result = result && ConvetEquExpression(and);
                }
                return result;
            }
            else
            {
                return ConvetEquExpression(expression);
            }
        }

        private bool ConvetEquExpression(string singleExp)
        {
            if (singleExp.Contains("=="))
            {
                var exps = singleExp.Split("==");
                var left = exps[0];
                var right = exps[1];
                return left == right;
            }
            else if (singleExp.Contains("!="))
            {
                var exps = singleExp.Split("!=");
                var left = exps[0];
                var right = exps[1];
                return left != right;
            }
            throw new ArgumentException("singleExp is ilegal");
        }
    }
}