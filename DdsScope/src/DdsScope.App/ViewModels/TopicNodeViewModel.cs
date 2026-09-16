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

    /// <summary>
    /// True when <paramref name="term"/> appears in either line the tree shows for this node,
    /// which is topic name plus type for a topic and writer name plus participant for a
    /// writer. Matching what is on screen rather than a separate set of fields is what makes
    /// the box predictable: whatever the user can read, they can search for.
    /// </summary>
    public bool MatchesSearch(string term) =>
        (Caption != null && Caption.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
        (Detail != null && Detail.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Takes on a freshly discovered description of the same topic.
    ///
    /// A rediscovered topic arrives as a new <see cref="DdsTopicInfo"/>: within one connection
    /// the adapter mutates the instance the node already holds, but a reconnect builds a new
    /// one. Refreshing from the instance held would keep the previous connection's type state,
    /// so a publisher that had stopped propagating its type still read as available while
    /// nothing decoded.
    /// </summary>
    public void AdoptTopic(DdsTopicInfo topic)
    {
        if (topic != null)
        {
            Topic = topic;
        }

        RefreshFromTopic();
    }

    /// <summary>Takes on a freshly discovered description of the same writer.</summary>
    public void AdoptWriter(DdsWriterInfo writer)
    {
        if (writer != null)
        {
            Writer = writer;
        }

        RefreshFromWriter();
    }

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
