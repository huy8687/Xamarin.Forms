using Xamarin.Forms.CustomAttributes;
using Xamarin.Forms.Internals;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Diagnostics;
using System.Collections.Generic;

#if UITEST
using Xamarin.UITest;
using NUnit.Framework;
#endif

namespace Xamarin.Forms.Controls.Issues
{
	[Preserve(AllMembers = true)]
	[Issue(
		IssueTracker.Bugzilla,
		52487,
		"ListView with Recycle + HasUnevenRows generates lots (and lots!) of content view",
		// https://bugzilla.xamarin.com/show_bug.cgi?id=52487
		PlatformAffected.iOS
	)]
	public class Bugzilla52487 : TestContentPage
	{
		public static IEnumerator<Color> Colors = ColorGenerator().GetEnumerator();
		static Color GetColor()
		{
			Colors.MoveNext();
			var color = Colors.Current;
			return color;
		}

		static Tuple<int, int, int> Mix = new Tuple<int, int, int>(255, 255, 100);
		static double s_colorDelta = 0;
		static IEnumerable<Color> ColorGenerator()
		{
			while (true)
			{
				s_colorDelta += 2 * Math.PI / 100;
				var r = (Math.Sin(s_colorDelta) + 1) / 2 * 255;
				var g = (Math.Sin(s_colorDelta * 2) + 1) / 2 * 255;
				var b = (Math.Sin(s_colorDelta * 3) + 1) / 2 * 255;

				if (Mix != null)
				{
					r = (r + Mix.Item1) / 2;
					g = (g + Mix.Item2) / 2;
					b = (b + Mix.Item3) / 2;
				}

				yield return Color.FromRgb((int)r, (int)g, (int)b);
			}
		}

		static int Random(int max)
			=> new Random().Next(max);

		public class Foo : INotifyPropertyChanged
		{
			const int DefaultHeight = 300;

			internal static void UpdateItems(int difference)
			{
				s_value += difference;

				foreach (var item in Items)
					item.Height = s_value;
			}

			internal static Foo[] Items 
				= Enumerable.Range(0, 100).Select(o => new Foo()).ToArray();

			internal static int s_alloc = 0;
			internal static int s_value = DefaultHeight;

			int _id;
			int _height;

			internal Foo()
			{
				_height = DefaultHeight;
				_id = s_alloc++;
				Update();
			}

			public int Id
				=> _id;
			public int Height
			{
				get { return _height; }
				set {
					_height = value;
					OnPropertyChanged();
				}
			}

			public event PropertyChangedEventHandler PropertyChanged;
 			protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
 				=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
 
			public override string ToString()
				=> $"{Id}, value={Height}";
		}

		[Preserve(AllMembers = true)]
		class FooViewCell : ViewCell
		{

			internal static int s_alloc = 0;
			internal static int s_free = 0;
			internal static int s_bind = 0;

			readonly int _id;
			readonly Label _label;
			int _bind = 0;

			public FooViewCell()
			{
				_id = s_alloc++;

				View = _label = new Label
				{
					BackgroundColor = GetColor(),
					VerticalTextAlignment = TextAlignment.Center,
					HorizontalTextAlignment = TextAlignment.Center,
				};

				_label.SetBinding(HeightRequestProperty, nameof(Foo.Height));
			}

			private Foo BindingContext
				=> (Foo)base.BindingContext;

			protected override void OnBindingContextChanged()
			{
				base.OnBindingContextChanged();

				s_bind++;
				_bind++;
			}

			protected override void OnAppearing()
			{
				_label.Text = ToString();
				Update();
			}

			~FooViewCell()
			{
				s_free++;

				// Update(); Would be off UI thread
			}

			public override string ToString()
				=> $"{BindingContext.Id} in {_id}v{_bind}, ask={_label.HeightRequest} got={View.Height}";
		}

		class MyDataTemplateSelector : DataTemplateSelector
		{
			internal static int s_count = 0;

			DataTemplate _fooDataTemplate;
			DataTemplate _barDataTemplate;

			public MyDataTemplateSelector()
			{
				// optimization requires that the DataTemplate use the .ctor that takes a type
				_fooDataTemplate = new DataTemplate(typeof(FooViewCell));
			}

			protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
			{
				s_count++;

				// optimization requires that DataTempalte be a function of the _type_ of the item
				if (!(item is Foo))
					throw new ArgumentException();

				return _fooDataTemplate;
			}
		}

		static Label s_cellCountsLabel = new Label();
		static Label s_dataCountsLabel = new Label();
		private static void Update()
		{
			s_cellCountsLabel.Text = 
				$"cells={FooViewCell.s_alloc}, selectTemplate={MyDataTemplateSelector.s_count}";

			s_dataCountsLabel.Text =
				$"data: value={Foo.s_value}, alloc={Foo.s_alloc}, bind={FooViewCell.s_bind}";
		}

		protected override void Init()
		{
			var stackLayout = new StackLayout();

			var buttonsGrid = new Grid();

			var gcButton = new Button() { Text = "GC" };
			gcButton.Clicked += (o, s) =>
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				Update();
			};
			buttonsGrid.Children.AddHorizontal(gcButton); 

			var addButton = new Button() { Text = "+25" };
			addButton.Clicked += (o, s) => {
				Foo.UpdateItems(25);
				Update();
			};
			buttonsGrid.Children.AddHorizontal(addButton);

			var subtractButton = new Button() { Text = "-25" };
			subtractButton.Clicked += (o, s) => {
				Foo.UpdateItems(-25);
				Update();
			};
			buttonsGrid.Children.AddHorizontal(subtractButton);

			var listView = new ListView(ListViewCachingStrategy.RecycleElementAndDataTemplate)
			{
				HasUnevenRows = true,
				// see https://github.com/xamarin/Xamarin.Forms/pull/994/files
				//RowHeight = 50,
				ItemsSource = Foo.Items,
				ItemTemplate = new MyDataTemplateSelector()
			};
			Content = new StackLayout {
				Children = {
					listView,
					buttonsGrid,
					s_cellCountsLabel,
					s_dataCountsLabel
				}
			};
		}

#if UITEST
		//[Test]
		//public void Bugzilla56896Test()
		//{
		//	RunningApp.WaitForElement(q => q.Marked(Instructions));
		//	var count = int.Parse(RunningApp.Query(q => q.Marked(ConstructorCountId))[0].Text);
		//	Assert.IsTrue(count < 100); // Failing test makes ~15000 constructor calls
		//	var time = int.Parse(RunningApp.Query(q => q.Marked(TimeId))[0].Text);
		//	Assert.IsTrue(count < 2000); // Failing test takes ~4000ms
		//}
#endif
	}
}