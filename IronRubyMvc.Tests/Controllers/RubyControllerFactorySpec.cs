using System;
using System.Web.Mvc;
using System.Web.Mvc.IronRuby.Controllers;
using System.Web.Mvc.IronRuby.Core;
using System.Web.Mvc.IronRuby.Extensions;
using System.Web.Routing;
using Moq;
using Moq.Mvc;
using Xunit;
using Microsoft.Scripting.Hosting;
using System.IO;
using System.Text;
using IronRuby.Builtins;

namespace System.Web.Mvc.IronRuby.Tests.Controllers
{
    [Concern(typeof(RubyControllerFactory))]
    public abstract class with_ironruby_mvc_and_routes_and_controller_file : InstanceContextSpecification<RubyControllerFactory>
    {
        /// <summary>
        /// Create a text file with content from value
        /// </summary>
        /// <param name="path">full text file path</param>
        /// <param name="value">text file content</param>
        protected void CreateFile(string path, string value)
        {
            FileStream fs = new FileStream(path, FileMode.Create);
            BinaryWriter bw = new BinaryWriter(fs);
            bw.Write(ASCIIEncoding.Default.GetBytes(value));
            bw.Flush();
            bw.Close();
            fs.Close();
        }
        
        /// <summary>
        /// create a default routes file in path
        /// </summary>
        /// <param name="path">full path name for the routes file to create</param>
        protected virtual void CreateRoutesFile(string path)
        {
            var script = new StringBuilder();

            script.AppendLine("#default routes");
            script.AppendLine("");
            script.AppendLine("$routes.ignore_route(\"{resource}.axd/{*pathInfo}\");");
            script.AppendLine("");
            script.AppendLine("$routes.map_route(\"default\", \"{controller}/{action}/{id}\",");
            script.AppendLine("  {:controller => 'Home', :action => 'index', :id => ''}");
            script.AppendLine(")");
            string value = script.ToString();

            CreateFile(path, value);
        }

        /// <summary>
        /// Creates a ruby controller file with content value in path
        /// </summary>
        /// <param name="path">file full path name</param>
        /// <param name="value">file content as string</param>
        protected virtual void CreateControllerFile(string path, string controllerName)
        {
            var script = new StringBuilder();
            script.AppendLine("class {0} < Controller".FormattedWith(controllerName));
            script.AppendLine("  def my_action");
            script.AppendLine("    $counter = $counter + 5");
            script.AppendLine("    \"Can't see ninjas\".to_clr_string");
            script.AppendLine("  end");
            script.AppendLine("end");
            string value = script.ToString();

            CreateFile(path, value);
        }

        private IControllerFactory originalFactory;

        protected IRubyEngine _rubyEngine;
        protected IPathProvider _pathProvider;
        protected RequestContext _requestContext;
        
        protected const string _controllerName = "My";
        protected const string _controllerClassName = "MyController";
        protected const string _mappedControllerPath = "MyController.rb";
        protected const string _virtualControllerPath = "~\\Controllers\\MyController.rb";

        protected override void EstablishContext()
        {
            //create a routes.rb and a ruby controller file in current directory
            CreateRoutesFile("routes.rb");
            CreateControllerFile(_mappedControllerPath, _controllerClassName);

            _pathProvider = An<IPathProvider>();
            //routes.rb
            _pathProvider.WhenToldTo(pp => pp.ApplicationPhysicalPath).Return(Environment.CurrentDirectory);
            _pathProvider.WhenToldTo(pp => pp.FileExists("~/routes.rb")).Return(true);
            _pathProvider.WhenToldTo(pp => pp.MapPath("~/routes.rb")).Return("routes.rb");
            //MyController.rb
            _pathProvider.WhenToldTo(pp => pp.FileExists(_virtualControllerPath)).Return(true);
            _pathProvider.WhenToldTo(pp => pp.MapPath(_virtualControllerPath)).Return(_mappedControllerPath);

            RouteTable.Routes.Clear();
            _requestContext = new RequestContext(new Mock<HttpContextBase>().Object, new RouteData());

            //save the original controller factory to avoid chaining all test factories
            originalFactory = ControllerBuilder.Current.GetControllerFactory();

            _rubyEngine = RubyEngine.InitializeIronRubyMvc(_pathProvider, "~/routes.rb");
        }

        protected override RubyControllerFactory CreateSut()
        {
            return (RubyControllerFactory)ControllerBuilder.Current.GetControllerFactory();
        }

        protected override void AfterEachObservation()
        {
            //restore the original controller factory to avoid chaining of all test factories
            ControllerBuilder.Current.SetControllerFactory(originalFactory);
        }
    }
    
    [Concern(typeof(RubyControllerFactory))]
    public class when_a_ruby_controller_needs_to_be_resolved : with_ironruby_mvc_and_routes_and_controller_file
    {
        private IController _controller;
        
        protected override void Because()
        {
            _controller = Sut.CreateController(_requestContext, _controllerName);
        }

        [Observation]
        public void should_have_returned_a_result()
        {
            _controller.ShouldNotBeNull();
        }

        [Observation]
        public void should_have_returned_a_controller()
        {
            _controller.ShouldBeAnInstanceOf<IController>();
        }

        [Observation]
        public void should_have_the_correct_controller_name()
        {
            (_controller as RubyController).ControllerName.ShouldBeEqualTo(_controllerName);
        }

        [Observation]
        public void should_have_the_correct_controller_class_name()
        {
            (_controller as RubyController).ControllerClassName.ShouldBeEqualTo(_controllerClassName);
        }
    }

    [Concern(typeof(RubyControllerFactory))]
    public class when_a_ruby_controller_was_resolved_twice : with_ironruby_mvc_and_routes_and_controller_file
    {
        private const string methodToFilter = "index";

        protected override void CreateControllerFile(string path, string controllerName)
        {
            var script = new StringBuilder();
            script.AppendLine("class {0} < Controller".FormattedWith(_controllerClassName));
            script.AppendLine("");
            script.AppendLine("  before_action :index do |context|");
            script.AppendLine("    context.request_context.http_context.response.write(\"Hello world<br />\")");
            script.AppendLine("  end");
            script.AppendLine("");
            script.AppendLine("  def index");
            script.AppendLine("    $counter = $counter + 5");
            script.AppendLine("    \"Can't see ninjas\".to_clr_string");
            script.AppendLine("  end");
            script.AppendLine("end");
            string value = script.ToString();

            CreateFile(path, value);
        }

        private int actionFiltersCountFirst;
        private int actionFiltersCountSecond;
        private IController _controller;

        protected override void Because()
        {
            _controller = Sut.CreateController(_requestContext, _controllerName);
            actionFiltersCountFirst = getActionFiltersCount(_controller);
            _controller = Sut.CreateController(_requestContext, _controllerName);
            actionFiltersCountSecond = getActionFiltersCount(_controller);
        }

        private int getActionFiltersCount(IController _controller)
        {
            var rubyType = ((RubyController)_controller).RubyType;
            var controllerFilters = (Hash)_rubyEngine.CallMethod(rubyType, "action_filters");

            int count = 0;
            controllerFilters.ToActionFilters(methodToFilter).ForEach(action => count++);
            return count;
        }

        [Observation]
        public void action_filters_count_should_be_equal()
        {
            actionFiltersCountSecond.ShouldBeEqualTo(actionFiltersCountFirst);
        }
    }


    //[Concern(typeof(RubyControllerFactory))]
    //public class when_a_controller_needs_to_be_resolved : InstanceContextSpecification<RubyControllerFactory>
    //{
    //    private IRubyEngine _rubyEngine;
    //    private IControllerFactory _controllerFactory;
    //    private IPathProvider _pathProvider;
    //    private RequestContext _requestContext;
    //    private const string _controllerName = "my_controller";
    //    private IController _controller;

    //    private string requirePath;

    //    protected override void EstablishContext()
    //    {
    //        _pathProvider = An<IPathProvider>();
    //        _rubyEngine = An<IRubyEngine>();
    //        _controllerFactory = An<IControllerFactory>();
    //        _requestContext = new RequestContext(new HttpContextMock().Object, new RouteData());

    //        _controllerFactory
    //            .WhenToldTo(factory => factory.CreateController(_requestContext, _controllerName))
    //            .Throw(new InvalidOperationException());

    //        _rubyEngine.WhenToldTo(eng => eng.RequireRubyFile(requirePath));
    //    }

    //    protected override RubyControllerFactory CreateSut()
    //    {
    //        return new RubyControllerFactory(_pathProvider, _controllerFactory, _rubyEngine);
    //    }

    //    protected override void Because()
    //    {
    //        _controller = Sut.CreateController(_requestContext, _controllerName);
    //    }

    //    [Observation]
    //    public void should_have_returned_a_result()
    //    {
    //        _controller.ShouldNotBeNull();
    //    }

    //    [Observation]
    //    public void should_have_returned_a_controller()
    //    {
    //        _controller.ShouldBeAnInstanceOf<IController>();
    //    }

    //    [Observation]
    //    public void it_should_have_called_the_ruby_engine()
    //    {
    //        _rubyEngine.WasToldTo(eng => eng.RequireRubyFile(requirePath)).OnlyOnce();
    //    }

    //    [Observation]
    //    public void it_should_have_require_path_from_ruby_engine()
    //    {
    //        requirePath.ShouldBeEqualTo("gaga_gaga");
    //    }

    //    [Observation]
    //    public void should_have_called_the_inner_controller_factory()
    //    {
    //        _controllerFactory.WasToldTo(factory => factory.CreateController(_requestContext, _controllerName)).OnlyOnce();
    //    }
    //}

    [Concern(typeof(RubyControllerFactory))]
    public class when_a_ruby_controller_needs_to_be_disposed: InstanceContextSpecification<RubyControllerFactory>
    {
        private IRubyEngine _rubyEngine;
        private IControllerFactory _controllerFactory;
        private IController _controller;
        private IPathProvider _pathProvider;

        protected override void EstablishContext()
        {
            _rubyEngine = An<IRubyEngine>();
            _controllerFactory = An<IControllerFactory>();
            _pathProvider = An<IPathProvider>();

            _controller = An<RubyController>();
        }

        protected override RubyControllerFactory CreateSut()
        {
            return new RubyControllerFactory(_pathProvider, _controllerFactory, _rubyEngine);
        }

        protected override void Because()
        {
            Sut.ReleaseController(_controller);
        }

        [Observation]
        public void should_have_called_dispose()
        {
            _controller.WasToldTo(c => ((IDisposable)c).Dispose()).OnlyOnce();
        }
        
    }
}