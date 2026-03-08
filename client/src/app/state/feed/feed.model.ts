export interface FeedEntry {
  id: string;
  text: string;
  timestamp: Date;
}

export interface FeedStateModel {
  entries: FeedEntry[];
  unreadCount: number;
}
