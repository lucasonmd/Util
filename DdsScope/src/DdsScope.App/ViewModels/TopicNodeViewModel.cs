using System.Collections.ObjectModel;
using DdsScope.App.Infrastructure;
using DdsScope.Dds.Abstractions;

namespace DdsScope.App.ViewModels;

/// <summary>A node of the topic/writer tree: either a topic (root) or one of its writers.</summary>
public sealed class TopicNodeViewModel : ObservableObject
{
    private string caption;
    private string detail;
    private bool isOnline = true;

    private TopicNodeViewModel(string caption)
    {
        this.caption = caption;
    }

    public static TopicNodeViewModel ForTopic(DdsTopicInfo topic)
    {
        var node = new TopicNodeViewModel(topic.TopicName) { Topic = topic };
        node.RefreshFromTopic();
        return node;
    }

    public static TopicNodeViewModel ForWriter(DdsWriterInfo writer)
    {
        var node = new TopicNodeViewModel(writer.DisplayName) { Writer = writer };
        node.RefreshFromWriter();
        return node;
    }

    public DdsTopicInfo Topic { get; private set; }

    public DdsWriterInfo Writer { get; private set; }

    public bool IsTopic => Topic != null;

    public ObservableCollection<TopicNodeViewModel> Children { get; } = new();

    public string Caption
    {
        get => caption;
        private set => Set(ref caption, value);
    }

    /// <summary>Secondary line: type name for topics, online state for writers.</summary>
    public string Detail
    {
        get => detail;
        private set => Set(ref detail, value);
    }

    public bool IsOnline
    {
        get => isOnline;
        private set => Set(ref isOnline, value);
    }

    public string TopicName => Topic?.TopicName ?? Writer?.TopicName;

    public string WriterId => Writer?.Id;

    public void RefreshFromTopic()
    {
        if (Topic == null)
        {
            return;
        }

        Caption = Topic.TopicName;
        Detail = Topic.TypeState == TopicTypeState.Available
            ? Topic.TypeName
            : Topic.TypeName + "  -  Type unavailable";
        IsOnline = Topic.TypeState == TopicTypeState.Available;
    }

    public void RefreshFromWriter()
    {
        if (Writer == null)
        {
            return;
        }

        Caption = Writer.DisplayName;
        Detail = Writer.IsOnline ? Writer.ParticipantId : "Offline  -  " + Writer.ParticipantId;
        IsOnline = Writer.IsOnline;
    }
}
