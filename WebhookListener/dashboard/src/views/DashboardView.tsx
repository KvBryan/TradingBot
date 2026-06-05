import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../context/AuthContext';
import { Trade, SystemLog } from '../types';
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';
import { getApiUrl } from '../utils/api';


export const DashboardView: React.FC = () => {
  const { token, logout } = useAuth();
  
  // App States
  const [trades, setTrades] = useState<Trade[]>([]);
  const [logs, setLogs] = useState<SystemLog[]>([]);
  const [capitalBalance, setCapitalBalance] = useState<number | null>(null);
  const [isDemo, setIsDemo] = useState<boolean | null>(null);
  const [isSignalRConnected, setIsSignalRConnected] = useState<boolean>(false);
  const [connectionError, setConnectionError] = useState<string | null>(null);
  
  // Loading & HTTP states
  const [isLoadingTrades, setIsLoadingTrades] = useState<boolean>(true);
  const [isLoadingLogs, setIsLoadingLogs] = useState<boolean>(true);
  const [errorTrades, setErrorTrades] = useState<string | null>(null);
  const [errorLogs, setErrorLogs] = useState<string | null>(null);

  // UI Interactive States
  const [activeLogId, setActiveLogId] = useState<number | null>(null);
  const [logFilter, setLogFilter] = useState<string>('ALL'); // ALL, INFO, ERROR
  const [notification, setNotification] = useState<{ message: string; type: 'info' | 'success' } | null>(null);
  const [activeTab, setActiveTab] = useState<string>('overview'); // overview, active, history, config
  
  const connectionRef = useRef<HubConnection | null>(null);

  // Display notification utility
  const triggerNotification = (message: string, type: 'info' | 'success' = 'info') => {
    setNotification({ message, type });
    setTimeout(() => {
      setNotification(null);
    }, 4000);
  };

  // HTTP Fetch function for Trades
  const fetchTrades = async () => {
    setIsLoadingTrades(true);
    setErrorTrades(null);
    try {
      const response = await fetch(getApiUrl('/api/v1/trades'), {
        headers: {
          'Authorization': `Bearer ${token}`,
        }
      });
      if (!response.ok) {
        throw new Error(`Error ${response.status}: ${response.statusText}`);
      }
      const data = await response.json();
      setTrades(data);
    } catch (err: any) {
      console.error("Error fetching trades:", err);
      setErrorTrades(err.message || 'Error al conectar con la API de trades');
    } finally {
      setIsLoadingTrades(false);
    }
  };

  // HTTP Fetch function for Logs
  const fetchLogs = async () => {
    setIsLoadingLogs(true);
    setErrorLogs(null);
    try {
      const response = await fetch(getApiUrl('/api/v1/logs'), {
        headers: {
          'Authorization': `Bearer ${token}`,
        }
      });
      if (!response.ok) {
        throw new Error(`Error ${response.status}: ${response.statusText}`);
      }
      const data = await response.json();
      setLogs(data);
    } catch (err: any) {
      console.warn("Logs endpoint could not be loaded, using simulated local logs:", err);
      setErrorLogs(err.message || 'Error al cargar logs del servidor');
      generateSimulatedLogs();
    } finally {
      setIsLoadingLogs(false);
    }
  };

  // HTTP Fetch function for Capital.com Balance
  const fetchBalance = async () => {
    try {
      const response = await fetch(getApiUrl('/api/v1/balance'), {
        headers: {
          'Authorization': `Bearer ${token}`,
        }
      });
      if (response.ok) {
        const data = await response.json();
        setCapitalBalance(data.balance);
        setIsDemo(data.isDemo);
      }
    } catch (err) {
      console.error("Error fetching balance:", err);
    }
  };

  // Soft-delete action on the backend
  const handleDeleteTrade = async (id: string) => {
    try {
      const response = await fetch(getApiUrl(`/api/v1/trades/${id}`), {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${token}`,
        }
      });
      if (!response.ok) {
        throw new Error(`Error ${response.status}: No se pudo eliminar el trade.`);
      }
      triggerNotification("Trade eliminado lógicamente", "success");
      fetchBalance();
    } catch (err: any) {
      console.error(err);
      triggerNotification(err.message || "Error al eliminar el trade", "info");
    }
  };

  // Generation of simulated logs if C# endpoint is missing or fails
  const generateSimulatedLogs = () => {
    const mockLogs: SystemLog[] = [
      {
        id: 1,
        timestamp: new Date(Date.now() - 3600000 * 2).toISOString(),
        logLevel: 'INFO',
        source: 'CapitalComService',
        message: 'Conexión exitosa con la API de Capital.com (Cuenta Demo). Balance inicial cargado.',
        stackTrace: null
      },
      {
        id: 2,
        timestamp: new Date(Date.now() - 3600000 * 1.5).toISOString(),
        logLevel: 'INFO',
        source: 'WebhookListener',
        message: 'Listener de Webhooks escuchando alertas de TradingView en /api/v1/webhook',
        stackTrace: null
      },
      {
        id: 3,
        timestamp: new Date(Date.now() - 60000 * 45).toISOString(),
        logLevel: 'WARNING',
        source: 'CapitalComService',
        message: 'La API de Capital.com reportó un ping de latencia elevado (380ms). Reintentando handshake.',
        stackTrace: null
      },
      {
        id: 4,
        timestamp: new Date(Date.now() - 60000 * 30).toISOString(),
        logLevel: 'INFO',
        source: 'TradingViewAlert',
        message: 'Webhook recibido de TradingView para BTCUSD - Estrategia: "EMA Cross 15m"',
        stackTrace: null
      },
      {
        id: 5,
        timestamp: new Date(Date.now() - 60000 * 28).toISOString(),
        logLevel: 'INFO',
        source: 'CapitalComService',
        message: 'Orden de compra ejecutada en Capital.com. ID: trd_984fha2 | Par: BTCUSD, Precio: 67340.5',
        stackTrace: null
      },
      {
        id: 6,
        timestamp: new Date(Date.now() - 60000 * 5).toISOString(),
        logLevel: 'ERROR',
        source: 'CapitalComService',
        message: 'Error al intentar cerrar orden en Capital.com para EURUSD - Sesión expirada temporalmente.',
        stackTrace: 'at WebhookListener.Features.Webhooks.CapitalComService.ClosePositionAsync(String positionId) in Features/Webhooks/CapitalComService.cs:line 182'
      }
    ];
    setLogs(mockLogs);
  };

  // SignalR connection setup
  const initSignalR = async (force = false) => {
    if (connectionRef.current) {
      const currentState = connectionRef.current.state;
      
      // Si no es un reinicio forzado y ya está conectado o conectándose, no hacemos nada.
      // Esto evita conexiones duplicadas causadas por el StrictMode de React 18.
      if (!force && (currentState === 'Connected' || currentState === 'Connecting')) {
        console.log("SignalR: Conexión activa o en proceso. Omitiendo inicialización duplicada.");
        return;
      }

      try {
        console.log("SignalR: Deteniendo conexión previa...");
        await connectionRef.current.stop();
      } catch (err) {
        console.warn("SignalR: Error al detener la conexión previa:", err);
      }
    }

    const customLogger = {
      log(logLevel: number, message: string) {
        // Ignorar logs ruidosos de detención/negociación limpia durante recargas de StrictMode o clics de Flush Handshake
        if (message && (
          message.includes("stopped during negotiation") || 
          message.includes("stopped before the start operation completed")
        )) {
          return;
        }
        
        // Mapear a logs de consola convencionales
        if (logLevel >= 4) {
          console.error("[SignalR]", message);
        } else if (logLevel === 3) {
          console.warn("[SignalR]", message);
        } else if (logLevel === 2) {
          console.info("[SignalR]", message);
        }
      }
    };

    const connection = new HubConnectionBuilder()
      .withUrl(getApiUrl('/hubs/trades'), {
        accessTokenFactory: () => token || ''
      })
      .configureLogging(customLogger)
      .withAutomaticReconnect()
      .build();

    connection.on('TradeUpdated', (updatedTrade: Trade) => {
      console.log('TradeUpdated recibido:', updatedTrade);
      triggerNotification(`Alerta: Trade ${updatedTrade.ticker} actualizado [${updatedTrade.status}]`, 'info');
      
      setTrades(prevTrades => {
        if (updatedTrade.isDeleted) {
          return prevTrades.filter(t => t.id !== updatedTrade.id);
        }
        
        const index = prevTrades.findIndex(t => t.id === updatedTrade.id);
        if (index !== -1) {
          const updatedList = [...prevTrades];
          updatedList[index] = updatedTrade;
          return updatedList;
        } else {
          return [updatedTrade, ...prevTrades];
        }
      });

      fetchBalance();

      setLogs(prevLogs => {
        const newLog: SystemLog = {
          id: Date.now(),
          timestamp: new Date().toISOString(),
          logLevel: updatedTrade.isDeleted ? 'WARNING' : (updatedTrade.status === 'LOSS' ? 'WARNING' : 'INFO'),
          source: 'SignalRHub',
          message: updatedTrade.isDeleted 
            ? `Trade ${updatedTrade.ticker} eliminado lógicamente del sistema.` 
            : `Evento 'TradeUpdated' recibido para ${updatedTrade.ticker} (${updatedTrade.direction}) - Estado: ${updatedTrade.status}. PnL: ${updatedTrade.profitLoss ?? 0}`,
          stackTrace: null
        };
        return [newLog, ...prevLogs];
      });
    });

    connection.onreconnecting(() => {
      setIsSignalRConnected(false);
      setConnectionError("Reconectando con el servidor de SignalR...");
    });

    connection.onreconnected(() => {
      setIsSignalRConnected(true);
      setConnectionError(null);
      triggerNotification('Conexión con SignalR reestablecida', 'success');
      fetchBalance();
      fetchTrades();
    });

    connection.onclose((error) => {
      setIsSignalRConnected(false);
      // Solo mostramos el error si la desconexión NO fue limpia (es decir, ocurrió un error real)
      if (error) {
        setConnectionError("Conexión con SignalR perdida.");
      } else {
        setConnectionError(null);
      }
    });

    connectionRef.current = connection;

    try {
      await connection.start();
      setIsSignalRConnected(true);
      setConnectionError(null);
      console.log("Conectado con SignalR Hub de Trades.");
      if (force) {
        triggerNotification('Conexión re-inicializada exitosamente', 'success');
      }
    } catch (err: any) {
      const isCleanAbort = err.name === 'AbortError' || 
                           (err.message && (
                             err.message.includes("stopped before the start operation completed") || 
                             err.message.includes("stopped during negotiation")
                           ));

      if (isCleanAbort) {
        console.log("SignalR: El inicio de la conexión fue cancelado o detenido limpiamente (ej. desmontaje o reinicio).");
      } else {
        console.error("Error al iniciar SignalR:", err);
        setIsSignalRConnected(false);
        setConnectionError("No se pudo establecer conexión con SignalR.");
      }
    }
  };

  useEffect(() => {
    fetchTrades();
    fetchLogs();
    fetchBalance();
    initSignalR();

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop();
      }
    };
  }, [token]);

  // Calculations for KPIs
  const closedTrades = trades.filter(t => t.status !== 'OPEN');
  const totalClosed = closedTrades.length;
  const wins = closedTrades.filter(t => t.status === 'WIN').length;
  
  const balanceNeto = closedTrades.reduce((sum, t) => sum + (t.profitLoss || 0), 0);
  const winRate = totalClosed > 0 ? (wins / totalClosed) * 100 : 0;
  const totalTradesCount = trades.length;

  const activeTrades = trades.filter(t => t.status === 'OPEN');
  const pastTrades = trades.filter(t => t.status !== 'OPEN');

  // Filter logs based on selection
  const filteredLogs = logs.filter(log => {
    if (logFilter === 'ALL') return true;
    if (logFilter === 'ERROR') return log.logLevel.toUpperCase() === 'ERROR';
    if (logFilter === 'INFO') return log.logLevel.toUpperCase() === 'INFO' || log.logLevel.toUpperCase() === 'WARNING' || log.logLevel.toUpperCase() === 'WARN';
    return true;
  });

  const handleSimulateWebhook = () => {
    const tickerList = ['BTCUSD', 'ETHUSD', 'SOLUSD', 'AAPL', 'EURUSD', 'TSLA'];
    const strategyList = ['EMA Cross 15m', 'Supertrend Scalper', 'RSI Divergence 4H', 'MACD Reversal'];
    const selectedTicker = tickerList[Math.floor(Math.random() * tickerList.length)];
    const selectedStrat = strategyList[Math.floor(Math.random() * strategyList.length)];
    
    const newLog: SystemLog = {
      id: Date.now(),
      timestamp: new Date().toISOString(),
      logLevel: 'INFO',
      source: 'TradingViewAlert',
      message: `Simulación Webhook recibida para ${selectedTicker} - Estrategia: "${selectedStrat}"`,
      stackTrace: null
    };

    setLogs(prev => [newLog, ...prev]);
    triggerNotification(`Simulación Webhook: ${selectedTicker}`, 'info');

    const randomPrice = 1.15923 + (Math.random() * 0.005);
    const newTrade: Trade = {
      id: Math.random().toString(36).substring(2, 11),
      ticker: selectedTicker,
      strategy: selectedStrat,
      direction: Math.random() > 0.5 ? 'BUY' : 'SELL',
      entryPrice: parseFloat(randomPrice.toFixed(5)),
      stopLoss: parseFloat((randomPrice - 0.003).toFixed(5)),
      takeProfit: parseFloat((randomPrice + 0.006).toFixed(5)),
      size: parseFloat((Math.random() * 5 + 0.1).toFixed(2)),
      status: 'OPEN',
      profitLoss: null,
      createdAt: new Date().toISOString(),
      isDeleted: false
    };

    setTrades(prev => [newTrade, ...prev]);
    if (capitalBalance !== null) {
      setCapitalBalance(prev => prev !== null ? prev - (newTrade.size * 5) : null);
    }
  };

  const handleSimulateCloseTrade = () => {
    const openTrades = trades.filter(t => t.status === 'OPEN');
    if (openTrades.length === 0) {
      triggerNotification('No hay trades activos para cerrar.', 'info');
      return;
    }

    const tradeToClose = openTrades[Math.floor(Math.random() * openTrades.length)];
    const isWin = Math.random() > 0.4;
    const pnl = isWin ? Math.floor(Math.random() * 500) + 50 : -(Math.floor(Math.random() * 300) + 20);
    
    const closedTrade: Trade = {
      ...tradeToClose,
      status: isWin ? 'WIN' : 'LOSS',
      profitLoss: pnl,
    };

    setTrades(prev => prev.map(t => t.id === tradeToClose.id ? closedTrade : t));
    
    setCapitalBalance(prev => prev !== null ? prev + pnl : null);

    const log: SystemLog = {
      id: Date.now(),
      timestamp: new Date().toISOString(),
      logLevel: isWin ? 'INFO' : 'WARNING',
      source: 'CapitalComService',
      message: `Orden cerrada para ${closedTrade.ticker} [${closedTrade.status}]. PnL: $${pnl}`,
      stackTrace: null
    };
    setLogs(prev => [log, ...prev]);
    triggerNotification(`Trade de ${closedTrade.ticker} cerrado con ${isWin ? 'GANANCIA' : 'PÉRDIDA'}`, 'info');
  };

  const clearLocalConsole = () => {
    setLogs([]);
    triggerNotification('Consola limpia', 'info');
  };

  // Calculate Segmented progress bar loading capacity ratio
  const getSegmentedProgressCount = () => {
    if (totalTradesCount === 0) return 0;
    const ratio = activeTrades.length / totalTradesCount;
    return Math.min(10, Math.ceil(ratio * 10));
  };

  // Map the last 9 closed trades to a real dynamic bar chart
  const lastTradesPnL = [...closedTrades].reverse().slice(0, 9).reverse();
  const chartBars = Array.from({ length: 9 }).map((_, idx) => {
    const trade = lastTradesPnL[idx];
    if (!trade) return { height: '15%', class: 'bg-primary/20', tooltip: 'No transaction data' };
    const pnl = trade.profitLoss ?? 0;
    const isWin = pnl >= 0;
    // Map absolute PnL value to a height between 15% and 100%
    const absVal = Math.min(Math.abs(pnl), 500); // capped at 500 for scaling visualizer
    const calculatedHeight = 15 + Math.floor((absVal / 500) * 85);
    return {
      height: `${calculatedHeight}%`,
      class: isWin ? 'bg-primary shadow-[0_0_8px_rgba(110,229,145,0.4)]' : 'bg-error shadow-[0_0_8px_rgba(255,180,171,0.4)]',
      tooltip: `${trade.ticker} (${trade.direction}): ${isWin ? '+' : ''}${pnl} USD`
    };
  });

  return (
    <div className="font-body-md text-on-surface bio-grid min-h-screen flex flex-col bg-surface-container-lowest select-none">
      
      {/* Toast Notification */}
      {notification && (
        <div className={`fixed bottom-4 right-4 z-50 flex items-center gap-sm px-sm py-sm rounded-sm border shadow-lg backdrop-blur-md transition-all duration-300 ${
          notification.type === 'success' 
            ? 'bg-[#121414]/95 border-primary/35 text-primary' 
            : 'bg-[#121414]/95 border-secondary/35 text-secondary'
        }`}>
          <div className={`w-1.5 h-1.5 rounded-full animate-ping ${notification.type === 'success' ? 'bg-primary' : 'bg-secondary'}`}></div>
          <span className="text-[10px] font-label-caps uppercase select-text">{notification.message}</span>
          <button 
            onClick={() => setNotification(null)}
            className="text-[9px] uppercase font-bold text-on-surface-variant hover:text-white bg-transparent border-none cursor-pointer pl-sm font-label-caps"
          >
            [X]
          </button>
        </div>
      )}
      
      {/* TopNavBar */}
      <header className="sticky top-0 z-50 bg-surface-container-low border-b border-outline-variant shadow-[0_4px_20px_-5px_rgba(5,26,20,0.5)] flex justify-between items-center w-full px-sm md:px-md py-sm">
        <div className="flex items-center gap-sm">
          <img 
            alt="Kronos Quant Logo" 
            className="h-10 w-10 object-contain" 
            src="/kronos_quant_logo.png"
          />
          <span className="font-headline-lg text-headline-lg-mobile md:text-headline-lg text-primary tracking-tighter uppercase select-none">
            FOREST PROTOCOL
          </span>
        </div>
        
        <div className="flex items-center gap-sm text-primary">
          <button 
            className="material-symbols-outlined hover:text-primary transition-colors duration-200 active:scale-95 bg-transparent border-none cursor-pointer p-1 text-primary text-xl select-none" 
            title="Broker Capital Status"
          >
            account_balance_wallet
          </button>
          <button 
            className="material-symbols-outlined hover:text-primary transition-colors duration-200 active:scale-95 bg-transparent border-none cursor-pointer p-1 text-primary text-xl relative select-none" 
            title="Notifications"
          >
            notifications
            {notification && <span className="absolute top-1 right-1 w-1.5 h-1.5 bg-error rounded-full animate-ping"></span>}
          </button>
          <button 
            onClick={logout} 
            className="material-symbols-outlined hover:text-error transition-colors duration-200 active:scale-95 bg-transparent border-none cursor-pointer p-1 text-primary text-xl select-none" 
            title="Sign Out Operator"
          >
            logout
          </button>
          <div className="h-8 w-8 rounded-full border border-primary/30 overflow-hidden select-none">
            <img 
              className="h-full w-full object-cover" 
              alt="Stylized research officer avatar" 
              src="https://lh3.googleusercontent.com/aida-public/AB6AXuDBowWths7PS4eFWZmPcWfRH7dIwfF2tqJ46lF8touONP4k_Ivhy0F4uyvzudDxqDU3dS1wAgX_1Un0p39O9RuXPsbKmKHE__QcbafsUNZvWmnErP1fsQeOi1anQuk7rGzRN19ubmaR3G8hZBTfYvOzxLNtQWjA4zAwhdz9s4eABfQAVkr_lylWt-CX2bLPTwHhLu8h7nJQKEIho66IEVhBVlsEpa3dasLHdGcpJwgLEPiTXhxRugXHJb0hOzF5HgtmOgoq5YFrzsE"
            />
          </div>
        </div>
      </header>

      {/* Main Layout Body */}
      <div className="flex flex-1 overflow-hidden">
        
        {/* SideNavBar */}
        <aside className="hidden md:flex flex-col h-full w-64 bg-surface-container-low border-r border-outline-variant shadow-[4px_0_24px_rgba(5,26,20,0.4)] p-sm gap-y-base min-h-[calc(100vh-64px)] select-none">
          <div className="mb-lg">
            <h3 className="font-label-caps text-label-caps text-primary uppercase">GROWTH TERMINAL</h3>
            <p className="text-[10px] text-on-surface-variant font-label-mono">v4.02-Biolume</p>
          </div>

          <nav className="flex-1 space-y-xs">
            <button 
              onClick={() => setActiveTab('overview')}
              className={`w-full flex items-center gap-sm p-sm rounded-lg font-bold transition-all cursor-pointer bg-transparent border-none text-left ${
                activeTab === 'overview' 
                  ? 'text-on-primary-container bg-primary-container' 
                  : 'text-on-surface-variant hover:bg-secondary-container/20 hover:text-secondary'
              }`}
            >
              <span className="material-symbols-outlined select-none text-[18px]">terminal</span>
              <span className="font-label-caps text-label-caps uppercase text-xs">Terminal</span>
            </button>

            <button 
              onClick={() => setActiveTab('active')}
              className={`w-full flex items-center gap-sm p-sm rounded-lg font-bold transition-all cursor-pointer bg-transparent border-none text-left ${
                activeTab === 'active' 
                  ? 'text-on-primary-container bg-primary-container' 
                  : 'text-on-surface-variant hover:bg-secondary-container/20 hover:text-secondary'
              }`}
            >
              <span className="material-symbols-outlined select-none text-[18px]">account_tree</span>
              <span className="font-label-caps text-label-caps uppercase text-xs">Growth Nodes ({activeTrades.length})</span>
            </button>

            <button 
              onClick={() => setActiveTab('history')}
              className={`w-full flex items-center gap-sm p-sm rounded-lg font-bold transition-all cursor-pointer bg-transparent border-none text-left ${
                activeTab === 'history' 
                  ? 'text-on-primary-container bg-primary-container' 
                  : 'text-on-surface-variant hover:bg-secondary-container/20 hover:text-secondary'
              }`}
            >
              <span className="material-symbols-outlined select-none text-[18px]">analytics</span>
              <span className="font-label-caps text-label-caps uppercase text-xs">Protocol Analytics ({pastTrades.length})</span>
            </button>

            <button 
              onClick={() => setActiveTab('config')}
              className={`w-full flex items-center gap-sm p-sm rounded-lg font-bold transition-all cursor-pointer bg-transparent border-none text-left ${
                activeTab === 'config' 
                  ? 'text-on-primary-container bg-primary-container' 
                  : 'text-on-surface-variant hover:bg-secondary-container/20 hover:text-secondary'
              }`}
            >
              <span className="material-symbols-outlined select-none text-[18px]">security</span>
              <span className="font-label-caps text-label-caps uppercase text-xs">Risk Management</span>
            </button>
          </nav>

          <div className="mt-auto space-y-sm">
            {/* Simulation injectors integrated inside bottom sidebar */}
            <div className="bg-surface-container-lowest/80 border border-outline-variant/60 p-xs space-y-xs rounded">
              <p className="font-label-mono text-[9px] text-primary/80 uppercase">Inyección de Eventos</p>
              <div className="grid grid-cols-2 gap-xs">
                <button
                  onClick={handleSimulateWebhook}
                  className="px-1.5 py-1 border border-primary/30 text-primary hover:bg-primary/10 font-label-caps text-[9px] uppercase transition-all duration-150 cursor-pointer"
                >
                  TV ALERT
                </button>
                <button
                  onClick={handleSimulateCloseTrade}
                  className="px-1.5 py-1 border border-outline text-on-surface hover:bg-surface-container font-label-caps text-[9px] uppercase transition-all duration-150 cursor-pointer disabled:opacity-30 disabled:pointer-events-none"
                  disabled={activeTrades.length === 0}
                >
                  LIQUIDATE
                </button>
              </div>
            </div>

            <button 
              onClick={() => {
                fetchTrades();
                fetchLogs();
                fetchBalance();
              }}
              className="w-full bg-primary hover:bg-primary-container text-on-primary py-sm font-label-caps text-label-caps tracking-widest active:scale-95 transition-all cursor-pointer border-none uppercase text-xs font-bold"
            >
              INITIALIZE PROTOCOL
            </button>
            
            <div className="flex items-center gap-xs text-[10px] text-primary/70">
              <span className="material-symbols-outlined text-[14px] animate-pulse">sensors</span>
              <span className="font-label-caps uppercase select-none">
                Status: {isSignalRConnected ? 'Online' : 'Offline'}
              </span>
            </div>
          </div>
        </aside>

        {/* Main Content Area */}
        <main className="flex-1 p-sm md:p-md overflow-y-auto terminal-scroll">
          <div className="max-w-7xl mx-auto space-y-md">
            
            {/* Connection Status & Error notifications */}
            {connectionError && (
              <div className="bg-error-container/20 border border-error text-error p-sm text-xs font-label-mono rounded flex items-center gap-xs select-text">
                <span className="material-symbols-outlined text-[16px]">warning</span>
                <span>[WARNING SYSTEM DISCONNECT] {connectionError} Make sure backend Port 8080 is reachable.</span>
              </div>
            )}

            {/* Performance Hero Grid */}
            <div className="grid grid-cols-1 md:grid-cols-4 gap-sm select-none">
              
              {/* Broker Net Balance (Real Dynamic Card) - In Large Font */}
              <div className="md:col-span-3 bg-surface-container p-sm md:p-md border border-outline-variant relative overflow-hidden">
                <div className="absolute top-0 right-0 p-sm opacity-10 pointer-events-none">
                  <span className="material-symbols-outlined text-6xl select-none">account_balance_wallet</span>
                </div>
                <h2 className="font-label-caps text-label-caps text-on-surface-variant mb-base select-none">BROKER NET BALANCE</h2>
                
                <div className="flex items-baseline gap-sm flex-wrap select-text">
                  <span className="text-3xl sm:text-display-lg text-primary font-headline-lg font-bold">
                    {capitalBalance !== null 
                      ? `$${capitalBalance.toLocaleString(undefined, { minimumFractionDigits: 2 })}` 
                      : '------'}
                  </span>
                  <span className="text-secondary font-label-mono text-xs select-none">
                    {isDemo ? 'DEMO ACCOUNT' : 'LIVE ACCOUNT'}
                  </span>
                </div>

                {/* Real-time closed trades P&L chart bars */}
                <div className="mt-md h-32 flex items-end gap-1 font-label-mono select-none">
                  {chartBars.map((bar, idx) => (
                    <div 
                      key={idx} 
                      style={{ height: bar.height }} 
                      className={`${bar.class} w-full rounded-sm transition-all duration-300 relative group cursor-help`}
                    >
                      {/* Tooltip on hover */}
                      <div className="absolute bottom-full left-1/2 -translate-x-1/2 bg-surface-container-highest border border-outline-variant text-[9px] px-2 py-0.5 rounded-sm opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap mb-1 z-20 pointer-events-none text-white font-mono shadow-md">
                        {bar.tooltip}
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* Aggregate APY and system performance stats */}
              <div className="bg-surface-container p-sm md:p-md border border-outline-variant flex flex-col justify-between select-none">
                <div>
                  <h2 className="font-label-caps text-label-caps text-on-surface-variant mb-base">AGGREGATE APY</h2>
                  <span className="font-headline-lg text-headline-lg text-primary select-all font-bold">
                    {balanceNeto >= 0 ? '+' : ''}{winRate.toFixed(2)}%
                  </span>
                  <p className="text-[10px] text-on-surface-variant font-label-mono mt-1">
                    PnL: {balanceNeto >= 0 ? '+' : ''}${balanceNeto.toLocaleString(undefined, { minimumFractionDigits: 2 })} USD
                  </p>
                </div>
                
                <div className="space-y-sm">
                  <div className="flex justify-between text-[10px] font-label-caps">
                    <span>WIN RATE</span>
                    <span>{wins} W / {totalClosed - wins} L ({winRate.toFixed(0)}%)</span>
                  </div>
                  <div className="w-full bg-surface-container-high h-1.5 overflow-hidden rounded-sm">
                    <div className="bg-primary h-full shadow-[0_0_8px_#6ee591]" style={{ width: `${winRate}%` }}></div>
                  </div>
                  <div className="flex justify-between text-[10px] font-label-caps">
                    <span>NODE CAPACITY ({activeTrades.length} / {totalTradesCount})</span>
                  </div>
                  <div className="segmented-progress">
                    {Array.from({ length: 10 }).map((_, idx) => (
                      <div 
                        key={idx} 
                        className={idx < getSegmentedProgressCount() ? 'active' : ''}
                      />
                    ))}
                  </div>
                </div>
              </div>

            </div>

            {/* Tab Navigation Views */}
            {activeTab === 'overview' && (
              <div className="grid grid-cols-1 lg:grid-cols-3 gap-md">
                
                {/* Active Strategies Table */}
                <div className="lg:col-span-2 bg-surface-container border border-outline-variant">
                  <div className="p-sm border-b border-outline-variant flex justify-between items-center select-none">
                    <h3 className="font-label-caps text-label-caps text-primary">ACTIVE STRATEGIES</h3>
                    <span 
                      onClick={fetchTrades}
                      className="material-symbols-outlined text-sm cursor-pointer hover:rotate-90 transition-transform select-none text-on-surface-variant"
                    >
                      sync
                    </span>
                  </div>
                  <div className="overflow-x-auto">
                    <table className="w-full text-left font-label-mono text-label-mono text-xs">
                      <thead className="text-on-surface-variant border-b border-outline-variant">
                        <tr>
                          <th className="p-sm md:p-md font-medium">STRATEGY_ID</th>
                          <th className="p-sm md:p-md font-medium">ASSET_PAIR</th>
                          <th className="p-sm md:p-md font-medium">ENTRY PRICE</th>
                          <th className="p-sm md:p-md font-medium">LOT SIZE</th>
                          <th className="p-sm md:p-md font-medium text-right">STATUS</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-outline-variant/30 select-text">
                        {errorTrades ? (
                          <tr>
                            <td colSpan={5} className="p-sm md:p-md text-center text-error font-label-mono uppercase">
                              [ERROR]: {errorTrades}
                            </td>
                          </tr>
                        ) : isLoadingTrades ? (
                          <tr>
                            <td colSpan={5} className="p-sm md:p-md text-center text-on-surface-variant font-label-mono uppercase">
                              Awaiting connection thread...
                            </td>
                          </tr>
                        ) : activeTrades.length === 0 ? (
                          <tr>
                            <td colSpan={5} className="p-sm md:p-md text-center text-on-surface-variant font-label-mono uppercase">
                              No active transactions detected.
                            </td>
                          </tr>
                        ) : (
                          activeTrades.map(trade => (
                            <tr key={trade.id} className="hover:bg-primary/5 transition-colors cursor-pointer group select-none">
                              <td className="p-sm md:p-md font-bold text-white uppercase select-all">{trade.strategy}</td>
                              <td className="p-sm md:p-md text-on-surface uppercase select-all">{trade.ticker}</td>
                              <td className="p-sm md:p-md text-primary font-bold select-all">{trade.entryPrice}</td>
                              <td className="p-sm md:p-md text-on-surface-variant select-all">{trade.size}</td>
                              <td className="p-sm md:p-md text-right">
                                <button 
                                  onClick={() => handleDeleteTrade(trade.id)}
                                  className="bg-primary-container/20 hover:bg-primary hover:text-on-primary text-primary-container px-xs py-[2px] border border-primary/30 rounded-sm text-[10px] uppercase cursor-pointer transition-all active:scale-95"
                                >
                                  LIQUIDATE
                                </button>
                              </td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                {/* Audit History Panel */}
                <div className="bg-surface-container border border-outline-variant flex flex-col select-none">
                  <div className="p-sm border-b border-outline-variant">
                    <h3 className="font-label-caps text-label-caps text-primary">AUDIT_HISTORY</h3>
                  </div>
                  <div className="flex-1 p-sm space-y-md terminal-scroll max-h-[400px] overflow-y-auto">
                    {pastTrades.length === 0 ? (
                      <div className="text-center py-md text-on-surface-variant font-label-mono uppercase text-xs">
                        No historical fills reported.
                      </div>
                    ) : (
                      pastTrades.slice(0, 10).map(trade => {
                        const isWin = trade.status === 'WIN';
                        return (
                          <div 
                            key={trade.id} 
                            className={`border-l-2 pl-sm py-xs transition-all hover:bg-surface-container-high/20 ${
                              isWin ? 'border-primary' : 'border-error'
                            }`}
                          >
                            <p className="text-[10px] text-outline font-label-mono">
                              {new Date(trade.createdAt).toLocaleTimeString()} - {trade.ticker}
                            </p>
                            <p className="text-label-mono text-xs select-all text-white font-bold">
                              {trade.direction} // LOT: {trade.size} // price: {trade.entryPrice}
                            </p>
                            <p className={`text-[11px] font-mono mt-0.5 font-bold ${isWin ? 'text-primary' : 'text-error'}`}>
                              PnL: {isWin ? '+' : ''}{trade.profitLoss} USD [{trade.status}]
                            </p>
                          </div>
                        );
                      })
                    )}
                  </div>
                  
                  <div className="p-sm bg-surface-container-highest/50">
                    <div className="flex items-center gap-xs">
                      <div className="w-2 h-2 rounded-full bg-primary animate-pulse"></div>
                      <span className="text-[10px] font-label-caps tracking-tighter text-on-surface-variant">
                        LISTENING FOR EVENTS...
                      </span>
                    </div>
                  </div>
                </div>

              </div>
            )}

            {activeTab === 'active' && (
              <div className="space-y-md select-none">
                <div className="p-sm bg-surface-container border border-outline-variant flex justify-between items-center select-none">
                  <h3 className="font-label-caps text-label-caps text-primary">GROWTH NODE LEDGER POSITION</h3>
                  <span className="text-[10px] text-on-surface-variant font-label-mono">
                    {activeTrades.length} ACTIVE POSITIONS
                  </span>
                </div>
                {activeTrades.length === 0 ? (
                  <div className="bg-surface-container/30 border border-dashed border-outline-variant/60 rounded-sm py-10 px-4 text-center select-none">
                    <span className="material-symbols-outlined text-4xl text-outline-variant">hourglass_empty</span>
                    <p className="text-sm text-on-surface-variant font-medium mt-sm">No active operations mapped in oracle ledger.</p>
                    <p className="text-[10px] text-outline mt-base">Use the injection controls in the sidebar to simulate events.</p>
                  </div>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-sm select-none">
                    {activeTrades.map(trade => (
                      <div 
                        key={trade.id} 
                        className="bg-surface-container border border-outline-variant hover:border-primary/50 transition-all duration-200 p-md relative overflow-hidden group shadow-md"
                      >
                        {/* Direction indicator */}
                        <div className={`absolute top-0 left-0 w-1 h-full ${trade.direction === 'BUY' ? 'bg-primary' : 'bg-error'}`}></div>

                        <div className="flex items-start justify-between mb-sm pl-xs">
                          <div>
                            <div className="flex items-center gap-xs">
                              <span className="text-sm font-bold text-white tracking-tight select-all">{trade.ticker}</span>
                              <span className={`text-[9px] font-label-caps px-1.5 py-0.5 rounded-sm border ${
                                trade.direction === 'BUY' 
                                  ? 'bg-primary/10 text-primary border-primary/20' 
                                  : 'bg-error/10 text-error border-error/20'
                              }`}>
                                {trade.direction}
                              </span>
                            </div>
                            <p className="text-[10px] text-on-surface-variant font-label-mono mt-[2px] select-all">
                              Strat: {trade.strategy}
                            </p>
                          </div>
                          
                          <button 
                            onClick={() => handleDeleteTrade(trade.id)}
                            className="p-1 hover:text-error hover:bg-error/10 border border-transparent hover:border-error/20 rounded-sm transition-all cursor-pointer bg-transparent text-on-surface-variant flex items-center justify-center"
                            title="Liquidate node"
                          >
                            <span className="material-symbols-outlined text-[16px]">delete</span>
                          </button>
                        </div>

                        <div className="pl-xs mb-sm flex items-center justify-between text-[11px] font-label-mono select-all">
                          <span className="text-on-surface-variant">Size Volume:</span>
                          <span className="text-secondary font-bold">{trade.size}</span>
                        </div>

                        {/* Price rules */}
                        <div className="pl-xs grid grid-cols-3 gap-xs bg-surface-container-lowest/80 border border-outline-variant/30 p-xs font-mono text-[10px] mb-xs select-all">
                          <div>
                            <div className="text-[8px] text-outline uppercase font-label-caps">Entry</div>
                            <div className="font-bold text-on-surface">{trade.entryPrice}</div>
                          </div>
                          <div>
                            <div className="text-[8px] text-outline uppercase font-label-caps text-error">Stop Loss</div>
                            <div className="font-bold text-error">{trade.stopLoss}</div>
                          </div>
                          <div>
                            <div className="text-[8px] text-outline uppercase font-label-caps text-primary">Take Profit</div>
                            <div className="font-bold text-primary">{trade.takeProfit}</div>
                          </div>
                        </div>

                        <div className="pl-xs flex items-center justify-between text-[9px] text-outline font-label-mono mt-sm select-all">
                          <span>ID: {trade.id.substring(0, 8)}...</span>
                          <span>{new Date(trade.createdAt).toLocaleTimeString()}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            {activeTab === 'history' && (
              <div className="bg-surface-container border border-outline-variant select-none">
                <div className="p-sm border-b border-outline-variant">
                  <h3 className="font-label-caps text-label-caps text-primary">FULL PROTOCOL HISTORICAL ARCHIVE</h3>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left font-label-mono text-label-mono text-xs">
                    <thead className="text-on-surface-variant border-b border-outline-variant">
                      <tr>
                        <th className="p-sm md:p-md font-medium">INSTRUMENT</th>
                        <th className="p-sm md:p-md font-medium">STRATEGY</th>
                        <th className="p-sm md:p-md font-medium">DIRECTION</th>
                        <th className="p-sm md:p-md font-medium">ENTRY PRICE</th>
                        <th className="p-sm md:p-md font-medium">LIMIT RULES (SL / TP)</th>
                        <th className="p-sm md:p-md font-medium">STATUS</th>
                        <th className="p-sm md:p-md font-medium text-right">REALIZED P&L ($)</th>
                        <th className="p-sm md:p-md font-medium text-center">DESTRUCTION</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-outline-variant/30 select-text">
                      {pastTrades.length === 0 ? (
                        <tr>
                          <td colSpan={8} className="p-sm md:p-md text-center text-on-surface-variant font-label-mono uppercase">
                            No archived operations logged.
                          </td>
                        </tr>
                      ) : (
                        pastTrades.map(trade => (
                          <tr key={trade.id} className="hover:bg-primary/5 transition-colors cursor-pointer select-none">
                            <td className="p-sm md:p-md font-bold text-white uppercase select-all">{trade.ticker}</td>
                            <td className="p-sm md:p-md text-on-surface select-all">{trade.strategy}</td>
                            <td className={`p-sm md:p-md font-bold select-all ${trade.direction === 'BUY' ? 'text-primary' : 'text-error'}`}>
                              {trade.direction}
                            </td>
                            <td className="p-sm md:p-md text-secondary font-bold select-all">{trade.entryPrice}</td>
                            <td className="p-sm md:p-md text-on-surface-variant text-[11px] select-all">
                              SL: {trade.stopLoss} | TP: {trade.takeProfit}
                            </td>
                            <td className="p-sm md:p-md">
                              <span className={`px-1.5 py-0.5 border text-[9px] font-label-caps uppercase ${
                                trade.status === 'WIN' 
                                  ? 'bg-primary-container/20 text-primary border-primary/30' 
                                  : 'bg-error-container/20 text-error border-error/30'
                              }`}>
                                {trade.status}
                              </span>
                            </td>
                            <td className={`p-sm md:p-md text-right font-bold select-all ${trade.status === 'WIN' ? 'text-primary' : 'text-error'}`}>
                              {trade.profitLoss && trade.profitLoss >= 0 ? '+' : ''}{trade.profitLoss}
                            </td>
                            <td className="p-sm md:p-md text-center">
                              <button 
                                onClick={() => handleDeleteTrade(trade.id)}
                                className="p-1 hover:text-error transition-colors cursor-pointer bg-transparent border-none text-on-surface-variant flex items-center justify-center mx-auto"
                              >
                                <span className="material-symbols-outlined text-[16px]">delete</span>
                              </button>
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {activeTab === 'config' && (
              <div className="bg-surface-container border border-outline-variant p-md space-y-md select-none">
                <h3 className="font-label-caps text-label-caps text-primary border-b border-outline-variant pb-xs mb-sm uppercase">
                  Growth System Tuning Parameters
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-md">
                  <div className="space-y-xs">
                    <h4 className="text-white font-bold text-sm uppercase">Re-initialize SignalR Hub</h4>
                    <p className="text-on-surface-variant text-xs font-mono select-all">Verify streaming channel ports, flush buffers, and re-establish C# data linkages.</p>
                    <button 
                      onClick={() => initSignalR(true)}
                      className="px-sm py-1.5 border border-primary/40 text-primary hover:bg-primary/5 font-label-caps text-[10px] uppercase transition-all duration-150 cursor-pointer rounded-sm active:scale-95 bg-transparent"
                    >
                      FLUSH HANDSHAKE
                    </button>
                  </div>
                  <div className="space-y-xs select-all">
                    <h4 className="text-white font-bold text-sm uppercase">Officer Access Gateway info</h4>
                    <p className="text-on-surface-variant text-xs font-mono">Node identifier NY-SEC-01 active. Secure JWT tunnel binds port 8080.</p>
                  </div>
                </div>
              </div>
            )}

            {/* Audit Logs System Terminal Console */}
            <div className="space-y-xs select-none">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-sm">
                <div className="flex items-center gap-sm">
                  <span className="material-symbols-outlined text-on-surface-variant text-[18px]">terminal</span>
                  <h3 className="font-label-caps text-label-caps text-on-surface-variant font-bold">Audit Logs &amp; Subsystem Runtime</h3>
                </div>
                <div className="hidden sm:block flex-1 border-t border-outline-variant/40 ml-sm"></div>
                
                <div className="flex items-center justify-between sm:justify-start gap-xs font-mono text-[10px] w-full sm:w-auto">
                  <div className="flex items-center gap-xs">
                    {(['ALL', 'INFO', 'ERROR'] as const).map(level => (
                      <button
                        key={level}
                        onClick={() => setLogFilter(level)}
                        className={`font-label-caps text-[9px] px-2 py-0.5 border cursor-pointer transition-all rounded-sm ${
                          logFilter === level 
                            ? 'border-primary text-primary bg-secondary-container/20' 
                            : 'border-outline-variant text-on-surface-variant hover:text-white bg-transparent'
                        }`}
                      >
                        {level}
                      </button>
                    ))}
                  </div>
                  
                  <button
                    onClick={clearLocalConsole}
                    className="p-1 border border-outline-variant text-on-surface-variant hover:text-white hover:border-primary rounded-sm transition-colors cursor-pointer ml-xs bg-transparent flex items-center justify-center"
                    title="Clear Log Terminal"
                  >
                    <span className="material-symbols-outlined text-[14px]">delete</span>
                  </button>
                </div>
              </div>

              <div className="bg-black border border-outline-variant p-md font-label-mono text-[12px] leading-relaxed relative group shadow-[0_4px_24px_rgba(5,26,20,0.6)]">
                <div className="absolute top-0 right-0 p-sm opacity-20 font-label-mono text-[9px] select-none">KRN_PRO_v4.2.0-Biolume</div>
                
                <div className="space-y-base overflow-y-auto h-[120px] pr-sm terminal-scroll bg-black/40" id="console-logs">
                  <p className="text-on-surface-variant">
                    <span className="text-white">[{new Date().toLocaleTimeString()}]</span> Connected to local logging interfaces. Active Node: NY-SEC-01.
                  </p>

                  {errorLogs && (
                    <p className="text-error select-all">
                      <span className="text-white">[{new Date().toLocaleTimeString()}]</span> [FAILSAFE ALERT] WebhookListener log handler report error: {errorLogs}
                    </p>
                  )}

                  {isLoadingLogs ? (
                    <p className="text-on-surface-variant">
                      <span className="text-white">[{new Date().toLocaleTimeString()}]</span> Listening for stream handshake packet...
                    </p>
                  ) : filteredLogs.length === 0 ? (
                    <p className="text-on-surface-variant italic">
                      -- No log registers matches active query filter [{logFilter}] --
                    </p>
                  ) : (
                    filteredLogs.map(log => {
                      const isError = log.logLevel.toUpperCase() === 'ERROR';
                      const hasStackTrace = log.stackTrace && log.stackTrace.trim().length > 0;
                      const isExpanded = activeLogId === log.id;

                      return (
                        <div 
                          key={log.id} 
                          className={`group border-b border-outline-variant/10 pb-1.5 last:border-b-0 hover:bg-surface-container-high/40 p-1 rounded-sm transition-all select-text ${
                            hasStackTrace ? 'cursor-pointer' : ''
                          }`}
                          onClick={() => hasStackTrace && setActiveLogId(isExpanded ? null : log.id)}
                        >
                          <div className="flex flex-col sm:flex-row sm:items-start gap-1 sm:gap-2">
                            <span className="text-white whitespace-nowrap">
                              [{new Date(log.timestamp).toLocaleTimeString()}]
                            </span>
                            <span className={`uppercase font-bold whitespace-nowrap ${isError ? 'text-error' : 'text-primary'}`}>
                              {log.logLevel}:
                            </span>
                            <span className="text-on-surface-variant font-bold whitespace-nowrap uppercase">
                              {log.source}:
                            </span>
                            <span className="text-on-surface break-all flex-1 font-mono">
                              {log.message}
                            </span>
                            {hasStackTrace && (
                              <span className="text-on-surface-variant text-[9px] shrink-0 hover:text-primary transition-colors select-none font-bold uppercase">
                                [{isExpanded ? 'Hide Trace' : 'Dump Trace'}]
                              </span>
                            )}
                          </div>
                          
                          {hasStackTrace && isExpanded && (
                            <div className="mt-1.5 p-2 bg-black border border-outline-variant text-error text-[10px] leading-relaxed whitespace-pre font-mono overflow-x-auto select-all">
                              <div className="text-on-surface-variant mb-1 border-b border-outline-variant pb-1 font-bold uppercase tracking-widest text-[8px] select-none">
                                Exception Call Trace:
                              </div>
                              {log.stackTrace}
                            </div>
                          )}
                        </div>
                      );
                    })
                  )}
                  
                  {/* Cursor Indicator */}
                  <p className="text-primary font-bold font-label-mono text-[11px] select-none">
                    <span className="text-white">[{new Date().toLocaleTimeString()}]</span> Awaiting transaction protocol delta scanning <span className="terminal-cursor text-primary"></span>
                  </p>
                </div>
              </div>
            </div>

            {/* Footer Stats Grid */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-sm">
              <div className="bg-surface-container p-sm border border-outline-variant group hover:border-primary transition-colors">
                <p className="text-[10px] text-on-surface-variant font-label-caps">NETWORK LATENCY</p>
                <p className="text-headline-lg text-primary font-headline-lg group-hover:translate-x-1 transition-transform">12ms</p>
              </div>
              <div className="bg-surface-container p-sm border border-outline-variant group hover:border-primary transition-colors">
                <p className="text-[10px] text-on-surface-variant font-label-caps">GAS EFFICIENCY</p>
                <p className="text-headline-lg text-on-surface font-headline-lg group-hover:translate-x-1 transition-transform">99.2%</p>
              </div>
              <div className="bg-surface-container p-sm border border-outline-variant group hover:border-primary transition-colors">
                <p className="text-[10px] text-on-surface-variant font-label-caps">THREAD COUNT</p>
                <p className="text-headline-lg text-on-surface font-headline-lg group-hover:translate-x-1 transition-transform">64X</p>
              </div>
              <div className="bg-surface-container p-sm border border-outline-variant group hover:border-primary transition-colors">
                <p className="text-[10px] text-on-surface-variant font-label-caps">ENVIRONMENT</p>
                <p className="text-headline-lg text-on-surface font-headline-lg group-hover:translate-x-1 transition-transform">L2-BIO</p>
              </div>
            </div>

          </div>
        </main>

      </div>
      
      {/* Mobile Nav Bar (Bottom) */}
      <nav className="md:hidden sticky bottom-0 z-50 bg-surface-container-low border-t border-outline-variant flex justify-around items-center py-xs px-md select-none">
        <button 
          onClick={() => setActiveTab('overview')}
          className={`flex flex-col items-center gap-base bg-transparent border-none cursor-pointer p-1 ${activeTab === 'overview' ? 'text-primary' : 'text-on-surface-variant'}`}
        >
          <span className="material-symbols-outlined select-none">terminal</span>
          <span className="text-[9px] font-label-caps">TERMINAL</span>
        </button>
        <button 
          onClick={() => setActiveTab('active')}
          className={`flex flex-col items-center gap-base bg-transparent border-none cursor-pointer p-1 ${activeTab === 'active' ? 'text-primary' : 'text-on-surface-variant'}`}
        >
          <span className="material-symbols-outlined select-none">account_tree</span>
          <span className="text-[9px] font-label-caps">NODES</span>
        </button>
        <button 
          onClick={() => setActiveTab('history')}
          className={`flex flex-col items-center gap-base bg-transparent border-none cursor-pointer p-1 ${activeTab === 'history' ? 'text-primary' : 'text-on-surface-variant'}`}
        >
          <span className="material-symbols-outlined select-none">analytics</span>
          <span className="text-[9px] font-label-caps">CHARTS</span>
        </button>
        <button 
          onClick={() => setActiveTab('config')}
          className={`flex flex-col items-center gap-base bg-transparent border-none cursor-pointer p-1 ${activeTab === 'config' ? 'text-primary' : 'text-on-surface-variant'}`}
        >
          <span className="material-symbols-outlined select-none">settings</span>
          <span className="text-[9px] font-label-caps">CONFIG</span>
        </button>
      </nav>

    </div>
  );
};

export default DashboardView;
