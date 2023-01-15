using XmlPreprocess;

namespace TestProject
{
    public class XmlPreprocessTest
    {
        [Fact]
        public void BasicTest()
        {
            Preprocessor preprocessor = new Preprocessor();
            var path = Path.Combine(Directory.GetCurrentDirectory(), @"xml\testCase\testCase1.xml");
            preprocessor.Process(path, null);
        }
    }
}