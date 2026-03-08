export class AddFeedEntry {
  static readonly type = '[Feed] Add Entry';
  constructor(public text: string) {}
}

export class MarkFeedRead {
  static readonly type = '[Feed] Mark Read';
}

export class ClearFeed {
  static readonly type = '[Feed] Clear';
}
