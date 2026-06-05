import React, { useState, useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { getApiUrl } from '../utils/api';

export const LoginView: React.FC = () => {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [subtitleText, setSubtitleText] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setIsLoading(true);

    try {
      const response = await fetch(getApiUrl('/api/v1/auth/login'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ email, password }),
      });

      if (response.ok) {
        const data = await response.json();
        if (data && data.token) {
          login(data.token);
        } else {
          setError('Error: No se recibió un token válido del servidor.');
        }
      } else if (response.status === 401) {
        setError('Credenciales incorrectas');
      } else {
        setError(`Error del servidor: Código ${response.status}`);
      }
    } catch (err) {
      setError('Error de conexión con el servidor.');
    } finally {
      setIsLoading(false);
    }
  };

  // Simulating terminal typing effect for the subtitle
  useEffect(() => {
    const originalText = 'Initialize secure authentication sequence.';
    let i = 0;
    setSubtitleText('');
    const timer = setInterval(() => {
      if (i < originalText.length) {
        setSubtitleText((prev) => prev + originalText.charAt(i));
        i++;
      } else {
        clearInterval(timer);
      }
    }, 40);
    return () => clearInterval(timer);
  }, []);



  return (
    <div className="font-body-md min-h-screen bg-background text-on-surface relative overflow-hidden flex flex-col items-center lg:items-start justify-center px-6 lg:px-xl py-lg selection:bg-primary/30 selection:text-primary">
      {/* Atmospheric Background */}
      <div className="fixed inset-0 terminal-grid pointer-events-none opacity-40"></div>
      <div className="scanline"></div>

      {/* Hero Background Visual */}
      <div className="fixed right-0 top-0 bottom-0 w-1/2 hidden lg:block overflow-hidden">
        <div className="absolute inset-0 bg-gradient-to-l from-surface-container-low to-transparent z-10"></div>
        <img
          className="h-full w-full object-cover grayscale opacity-20"
          alt="Advanced digital botany"
          src="https://lh3.googleusercontent.com/aida-public/AB6AXuBYYegjPYg8dhZN6n2awBLCTYvgWj0fh0DE3rnWRN63sSswtW_wpKfhQROhSFidQ42YvQQDcW_n8Krj8IrSPONdYT2CIK3TbDurA5fVl2zkJvMbtjKLfIaDaCP4cUNUbpH6v5TxnQGbywXnwlXWrVFSj9KG7h-rxXJrvM7s2TT0rrXoJGrd5kcv-aZL6arm_rIAwTH6CU9QmmGhN9SMvmwo0ifA_heiOErlu4ccY2NLDz5_s4U4qerszR5gIMCVRffRj8_UtmfdoMg"
        />
        {/* Floating Data Nodes Overlay */}
        <div className="absolute inset-0 z-20 flex items-center justify-center data-stream">
          <div className="w-full max-w-lg space-y-4 opacity-10 font-label-mono text-[10px] uppercase text-primary">
            <div className="flex justify-between border-b border-primary/20 pb-1">
              <span>node_initialization</span>
              <span>0x4F2A</span>
            </div>
            <div className="flex justify-between border-b border-primary/20 pb-1">
              <span>growth_protocol_active</span>
              <span>stable</span>
            </div>
            <div className="flex justify-between border-b border-primary/20 pb-1">
              <span>synaptic_sync_status</span>
              <span>98.2%</span>
            </div>
            <div className="flex justify-between border-b border-primary/20 pb-1">
              <span>biolume_levels</span>
              <span>optimal</span>
            </div>
            <div className="flex justify-between border-b border-primary/20 pb-1">
              <span>quant_encryption_key</span>
              <span>verified</span>
            </div>
          </div>
        </div>
      </div>

      {/* Main Content Canvas */}
      <div className="relative z-30 w-full max-w-md space-y-md">
        {/* Branding Area */}
        <div className="space-y-sm">
          <div className="flex items-center gap-xs">
            <img
              alt="Kronos Quant Logo"
              className="h-10 w-10 object-contain"
              src="/kronos_quant_logo.png"
            />
            <h1 className="font-headline-lg text-headline-lg text-primary tracking-tighter uppercase font-sora">
              Kronos Quant
            </h1>
          </div>
          <div className="flex items-center gap-sm">
            <span className="bg-primary/10 text-primary px-2 py-0.5 rounded-sm font-label-caps text-label-caps border border-primary/30">
              Growth Terminal
            </span>
            <span className="text-on-surface-variant font-label-mono text-label-mono opacity-60">
              v4.02-Biolume
            </span>
          </div>
        </div>

        {/* Login Form Pod */}
        <div
          className="bg-surface-container-low border border-outline-variant p-sm sm:p-md rounded-lg shadow-[0_20px_50px_rgba(5,26,20,0.6)]"
        >
          <div className="mb-md">
            <h2 className="font-headline-lg text-headline-lg text-on-surface mb-xs">
              Protocol Access
            </h2>
            <p className="text-on-surface-variant font-label-mono text-label-mono min-h-[24px]">
              {subtitleText}
            </p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-md">
            {/* Operator Identity Field */}
            <div className="space-y-base group">
              <label className="font-label-caps text-label-caps text-on-surface-variant group-focus-within:text-primary transition-colors">
                Operator Identity
              </label>
              <div className="relative">
                <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant text-[18px]">
                  fingerprint
                </span>
                <input
                  required
                  type="email"
                  className="w-full bg-surface-container-highest border-b border-outline-variant focus:border-primary focus:ring-0 text-primary font-label-mono pl-10 py-3 transition-all placeholder:text-outline-variant rounded-sm outline-none"
                  placeholder="auditor@tradingbot.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={isLoading}
                />
              </div>
            </div>

            {/* Access Key Field */}
            <div className="space-y-base group">
              <div className="flex justify-between items-end">
                <label className="font-label-caps text-label-caps text-on-surface-variant group-focus-within:text-primary transition-colors">
                  Access Key
                </label>
              </div>
              <div className="relative">
                <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant text-[18px]">
                  terminal
                </span>
                <input
                  required
                  type="password"
                  className="w-full bg-surface-container-highest border-b border-outline-variant focus:border-primary focus:ring-0 text-primary font-label-mono pl-10 py-3 transition-all placeholder:text-outline-variant rounded-sm outline-none"
                  placeholder="••••••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={isLoading}
                />
              </div>
            </div>

            {error && (
              <div className="text-xs font-label-mono text-error bg-error/5 border border-error p-sm rounded-sm flex items-center gap-xs">
                <span className="material-symbols-outlined text-[16px] text-error flex-shrink-0">
                  warning
                </span>
                <span>{error}</span>
              </div>
            )}

            {/* Action Pod */}
            <div className="pt-sm space-y-sm">
              <button
                type="submit"
                disabled={isLoading}
                className="w-full bg-primary-container text-on-primary-container font-label-caps py-4 rounded-sm flex items-center justify-center gap-sm hover:bg-primary hover:text-on-primary transition-all active:scale-95 duration-100 group cursor-pointer disabled:opacity-50"
              >
                {isLoading ? (
                  <>
                    <span className="w-4 h-4 rounded-full border-2 border-t-transparent border-on-primary-container animate-spin"></span>
                    <span>Initializing...</span>
                  </>
                ) : (
                  <>
                    <span>Initialize Protocol</span>
                    <span className="material-symbols-outlined group-hover:translate-x-1 transition-transform">
                      bolt
                    </span>
                  </>
                )}
              </button>
            </div>
          </form>
        </div>

        {/* System Status Footer */}
        <div className="flex flex-col sm:flex-row gap-sm pt-sm justify-between items-center opacity-70">
          <div className="flex items-center gap-sm">
            <div className="w-2 h-2 rounded-full bg-primary animate-pulse"></div>
            <span className="font-label-mono text-[10px] text-on-surface-variant uppercase">
              Quantum Shield: Active
            </span>
          </div>
          <div className="flex items-center gap-md">
            <span className="font-label-mono text-[10px] text-on-surface-variant">
              NODE_09: STABLE
            </span>
            <span className="font-label-mono text-[10px] text-on-surface-variant">
              LATENCY: 14MS
            </span>
          </div>
        </div>

        {/* Utility Links */}
        <footer className="pt-lg border-t border-outline-variant w-full max-w-md flex flex-col sm:flex-row justify-between items-center gap-sm">
          <div className="flex gap-md">
            <a
              className="font-label-mono text-[10px] text-on-surface-variant hover:text-primary transition-colors"
              href="#"
            >
              Documentation
            </a>
            <a
              className="font-label-mono text-[10px] text-on-surface-variant hover:text-primary transition-colors"
              href="#"
            >
              Support
            </a>
          </div>
          <p className="font-label-mono text-[10px] text-on-surface-variant/40 order-first sm:order-last">
            © 2026 KRONOS QUANT GROUP
          </p>
        </footer>
      </div>
    </div>
  );
};

export default LoginView;


