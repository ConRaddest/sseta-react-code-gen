"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  PmvrSystemUserUpdateRequest,
} from "@/types/api.types"

interface PmvrSystemUserContextType {
  // State
  // Operations
  update: (data: PmvrSystemUserUpdateRequest) => Promise<boolean>
}

// Undefined default is intentional — enforced by the usePmvrSystemUser hook below.
const PmvrSystemUserContext = createContext<PmvrSystemUserContextType | undefined>(undefined)

export function PmvrSystemUserProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({})

  const update = async (data: PmvrSystemUserUpdateRequest): Promise<boolean> => {
    try {
      await Api.PMVR.SystemUser.update(data)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <PmvrSystemUserContext.Provider
      value={{
        ...state,
        update,
      }}
    >
      {children}
    </PmvrSystemUserContext.Provider>
  )
}

// Throws if used outside of PmvrSystemUserProvider to catch missing provider wrapping early.
export function usePmvrSystemUser() {
  const context = useContext(PmvrSystemUserContext)
  if (context === undefined) {
    throw new Error("usePmvrSystemUser must be used within a PmvrSystemUserProvider")
  }
  return context
}
