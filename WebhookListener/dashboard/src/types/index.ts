export interface Trade {
  id: string;
  ticker: string;
  strategy: string;
  direction: 'BUY' | 'SELL';
  entryPrice: number;
  stopLoss: number;
  takeProfit: number;
  size: number;
  status: 'OPEN' | 'WIN' | 'LOSS';
  profitLoss: number | null;
  createdAt: string;
  isDeleted: boolean;
}

export interface SystemLog {
  id: number;
  timestamp: string;
  logLevel: string;
  source: string;
  message: string;
  stackTrace: string | null;
}
